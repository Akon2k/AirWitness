using System;
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
    private readonly ILogger<MonitoringSchedulerService> _logger;
    private readonly HashSet<string> _schedulerOwnedStreams = new();

    public MonitoringSchedulerService(
        IServiceProvider serviceProvider,
        WorkerOrchestrator orchestrator,
        ILogger<MonitoringSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _orchestrator = orchestrator;
        _logger = logger;
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
            bool isInsideWindow = now >= schedule.StartTime && now <= schedule.EndTime;
            bool isInsideBufferWindow = bufferTime >= schedule.StartTime && now <= schedule.EndTime;

            bool isRunning = _orchestrator.IsRunning(station.StreamUrl);

            // CASO 1: Iniciar monitoreo (estamos en ventana de buffer y no está corriendo)
            if (isInsideBufferWindow && !isRunning)
            {
                _logger.LogInformation("[SCHEDULER] Iniciando radio {Station} por programación ({Start}-{End})", station.Name, schedule.StartTime, schedule.EndTime);
                
                _orchestrator.StartMonitor(station.DefaultMasterPath ?? "", station.StreamUrl);
                _schedulerOwnedStreams.Add(station.StreamUrl);
            }
            // CASO 2: Detener monitoreo (ha pasado la hora de fin y fue iniciado por el scheduler)
            else if (!isInsideWindow && isRunning && _schedulerOwnedStreams.Contains(station.StreamUrl))
            {
                _logger.LogInformation("[SCHEDULER] Deteniendo radio {Station} (Fin de bloque programado)", station.Name);
                
                _orchestrator.StopMonitor(station.StreamUrl);
                _schedulerOwnedStreams.Remove(station.StreamUrl);
            }
        }
    }
}
