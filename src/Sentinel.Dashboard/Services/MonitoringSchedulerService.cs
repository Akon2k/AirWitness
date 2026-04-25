using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentinel.Dashboard.Data;
using Sentinel.Dashboard.Models.Data;

namespace Sentinel.Dashboard.Services;

public class MonitoringSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkerOrchestrator _orchestrator;
    private readonly IAlertService _alertService;
    private readonly ILogger<MonitoringSchedulerService> _logger;
    private readonly ConcurrentDictionary<string, byte> _schedulerOwnedStreams = new();

    public MonitoringSchedulerService(
        IServiceProvider serviceProvider,
        WorkerOrchestrator orchestrator,
        IAlertService alertService,
        ILogger<MonitoringSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _orchestrator = orchestrator;
        _alertService = alertService;
        _logger = logger;
        
        _orchestrator.OnNewResult += HandleMatchResult;
        _orchestrator.OnLog += HandleOrchestratorLog;
    }

    private void HandleOrchestratorLog(string message, string sourceUrl)
    {
        if (message.Contains("Error", StringComparison.OrdinalIgnoreCase) || 
            message.Contains("Failed", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () => {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var station = await db.RadioStations.FirstOrDefaultAsync(r => r.StreamUrl == sourceUrl);
                
                if (station != null)
                {
                    await _alertService.NotifyStreamFailureAsync(station, message);
                }
            });
        }
    }

    private void HandleMatchResult(MatchResult result)
    {
        if (result.Match && _schedulerOwnedStreams.ContainsKey(result.Source))
        {
            _logger.LogInformation("[SCHEDULER] Captura exitosa en {Source}. Esperando 7s delta antes de liberar...", result.Source);
            
            // Usamos un Fire-and-forget con delay para no bloquear el hilo de eventos sincronizados
            _ = Task.Run(async () => {
                await Task.Delay(7000); 
                _orchestrator.StopMonitor(result.Source);
                _schedulerOwnedStreams.TryRemove(result.Source, out _);
                _logger.LogInformation("[SCHEDULER] Monitor detenido para {Source} tras delta de 7s.", result.Source);
            });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Smart Monitoring Scheduler Service iniciado (Buffer: 5 min).");

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessSchedulesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en ciclo interno de programación. Reintentando en 1 minuto...");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scheduler Service deteniéndose por solicitud del host.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FALLO CRÍTICO en el Servicio de Programación. El Host continuará pero la automatización puede fallar.");
        }
    }

    private async Task ProcessSchedulesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var bufferTime = now.AddMinutes(5); // Adelanto de 5 minutos solicitado por el usuario

        // Obtener todos los horarios activos
        var activeSchedules = await db.MonitoringSchedules
            .Include(s => s.RadioStation)
            .Where(s => s.IsActive && s.RadioStation != null)
            .ToListAsync(ct);

        foreach (var schedule in activeSchedules)
        {
            var station = schedule.RadioStation!;
            bool isInsideWindow = (now >= schedule.StartTime.AddMinutes(-5)) && (now <= schedule.EndTime);
            bool isInsideBufferWindow = isInsideWindow; // Sincronizado para evitar rebote de start/stop

            bool isRunning = _orchestrator.IsRunning(station.StreamUrl);

            // CASO 1: Iniciar monitoreo (estamos en ventana de buffer y no está corriendo)
            if (isInsideBufferWindow && !isRunning)
            {
                _logger.LogInformation("[SCHEDULER] Iniciando radio {Station} por programación ({Start}-{End})", station.Name, schedule.StartTime, schedule.EndTime);
                var masterTitle = string.IsNullOrEmpty(station.DefaultMasterPath) ? "Comercial Programado" : System.IO.Path.GetFileNameWithoutExtension(station.DefaultMasterPath);
                _orchestrator.StartMonitor(station.DefaultMasterPath ?? "", station.StreamUrl, masterTitle, station.Name);
                _schedulerOwnedStreams.TryAdd(station.StreamUrl, 1);
            }
            // CASO 2: Detener monitoreo (ha pasado la hora de fin y fue iniciado por el scheduler)
            else if (!isInsideWindow && isRunning && _schedulerOwnedStreams.ContainsKey(station.StreamUrl))
            {
                _logger.LogInformation("[SCHEDULER] Deteniendo radio {Station} (Fin de bloque programado)", station.Name);
                
                _orchestrator.StopMonitor(station.StreamUrl);
                _schedulerOwnedStreams.TryRemove(station.StreamUrl, out _);
            }
        }
    }
}
