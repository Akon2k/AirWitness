using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sentinel.Dashboard.Data;
using Sentinel.Dashboard.Models.Data;

namespace Sentinel.Dashboard.Services;

public class RadioConfigService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _configPath;

    public RadioConfigService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        
        var dataDir = Path.Combine(Environment.CurrentDirectory, "wwwroot", "data");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        _configPath = Path.Combine(dataDir, "radio_config.json");
    }

    /// <summary>
    /// Carga la configuración desde PostgreSQL. 
    /// Si la DB está vacía, intenta migrar desde el JSON antiguo.
    /// </summary>
    public async Task<List<RadioTask>> LoadConfigAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stations = await db.RadioStations
            .Include(s => s.Schedules)
            .ToListAsync();
        
        // Si no hay estaciones en DB, intentamos migrar desde JSON una sola vez
        if (stations.Count == 0 && File.Exists(_configPath))
        {
            await AutoMigrateFromJsonAsync();
            stations = await db.RadioStations.Include(s => s.Schedules).ToListAsync();
        }

        return stations.Select(ToTask).ToList();
    }

    private RadioTask ToTask(RadioStation s) => new RadioTask
    {
        Id = s.Id.ToString(),
        Name = s.Name,
        StreamUrl = s.StreamUrl,
        MasterPath = s.DefaultMasterPath ?? "",
        City = s.City ?? "",
        Region = s.Region ?? "",
        Frequency = s.Frequency ?? "",
        Category = s.Category ?? "Radio",
        Schedules = s.Schedules.ToList()
    };

    public async Task SaveStationAsync(RadioTask task)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Intentar parsear el ID (si es entero, es DB; si es GUID, es nuevo)
        int.TryParse(task.Id, out int id);

        var station = id > 0 ? await db.RadioStations.FindAsync(id) : null;
        
        if (station == null)
        {
            station = new RadioStation();
            db.RadioStations.Add(station);
        }

        station.Name = task.Name;
        station.StreamUrl = task.StreamUrl;
        station.City = task.City;
        station.Region = task.Region;
        station.Frequency = task.Frequency;
        station.Category = task.Category;
        station.DefaultMasterPath = task.MasterPath;
        station.IsActive = true;

        await db.SaveChangesAsync();
    }

    public async Task DeleteStationAsync(string taskId)
    {
        if (!int.TryParse(taskId, out int id)) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var station = await db.RadioStations
                .Include(s => s.Schedules)
                .Include(s => s.Matches)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (station != null)
            {
                // Eliminar dependencias manualmente por seguridad de FK
                if (station.Schedules != null) db.MonitoringSchedules.RemoveRange(station.Schedules);
                if (station.Matches != null) db.MatchRecords.RemoveRange(station.Matches);
                
                // Limpiar logs de notificaciones vinculados
                var logs = await db.NotificationLogs.Where(l => l.RadioStationId == id).ToListAsync();
                if (logs.Any()) db.NotificationLogs.RemoveRange(logs);
                
                db.RadioStations.Remove(station);
                await db.SaveChangesAsync();
                Console.WriteLine($"[BORRADO] Estación {id} ({station.Name}) eliminada exitosamente.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falló el borrado de la estación {id}: {ex.Message}");
            throw; 
        }
    }

    /// <summary>
    /// Exporta la configuración actual de la DB a un archivo JSON.
    /// </summary>
    public async Task<string> ExportToJsonAsync()
    {
        var tasks = await LoadConfigAsync();
        var json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, json);
        return _configPath;
    }

    /// <summary>
    /// Importa estaciones desde un JSON y las mezcla/reemplaza en la base de datos.
    /// </summary>
    public async Task ImportFromJsonAsync(string json)
    {
        var tasks = JsonSerializer.Deserialize<List<RadioTask>>(json);
        if (tasks == null) return;

        foreach (var task in tasks)
        {
            await SaveStationAsync(task);
        }
    }

    // --- MÉTODOS DE PROGRAMACIÓN (SCHEDULES) ---

    public async Task<List<MonitoringSchedule>> GetSchedulesAsync(int stationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.MonitoringSchedules
            .Where(s => s.RadioStationId == stationId)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task SaveScheduleAsync(MonitoringSchedule schedule)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (schedule.Id == 0)
        {
            db.MonitoringSchedules.Add(schedule);
        }
        else
        {
            db.Entry(schedule).State = EntityState.Modified;
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteScheduleAsync(int scheduleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedule = await db.MonitoringSchedules.FindAsync(scheduleId);
        if (schedule != null)
        {
            db.MonitoringSchedules.Remove(schedule);
            await db.SaveChangesAsync();
        }
    }

    public async Task<bool> ValidateStreamUrlAsync(string url)
    {
        try
        {
            using var handler = new HttpClientHandler { 
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true 
            };
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task AutoMigrateFromJsonAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            var tasks = JsonSerializer.Deserialize<List<RadioTask>>(json);
            if (tasks != null)
            {
                foreach (var t in tasks) await SaveStationAsync(t);
            }
            // Marcamos el archivo como migrado para no repetir
            File.Move(_configPath, _configPath + ".migrated", true);
        }
        catch { }
    }

    public async Task<string> GetConfigJsonAsync()
    {
        var tasks = await LoadConfigAsync();
        return System.Text.Json.JsonSerializer.Serialize(tasks, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
