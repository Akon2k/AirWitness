using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Dashboard.Models.Data;

namespace Sentinel.Dashboard.Services;

public class MatchResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("match")]
    public bool Match { get; set; }

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    [JsonPropertyName("offset_seconds")]
    public decimal OffsetSeconds { get; set; }

    [JsonPropertyName("stream_elapsed_seconds")]
    public decimal StreamElapsedSeconds { get; set; }

    [JsonPropertyName("master_duration")]
    public decimal MasterDuration { get; set; }

    [JsonPropertyName("evidence_file")]
    public string? EvidenceFile { get; set; }
}

public class WorkerOrchestrator : IDisposable
{
    private readonly NativeAudioMonitor _audioMonitor;
    private readonly IAlertService _alertService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, CancellationTokenSource> _activeWorkers = new();
    
    public event Action<MatchResult>? OnNewResult;
    public event Action<string, string>? OnLog; // Mensaje, SourceUrl
    public event Action? OnStatusChanged;

    public WorkerOrchestrator(NativeAudioMonitor audioMonitor, IAlertService alertService, IServiceProvider serviceProvider)
    {
        _audioMonitor = audioMonitor;
        _alertService = alertService;
        _serviceProvider = serviceProvider;
        
        _audioMonitor.OnTelemetry += (url, msg) => {
            OnLog?.Invoke(msg, url);
        };
        
        _audioMonitor.OnMatchFound += (payload) => {
            var result = new MatchResult {
                Timestamp = payload.Timestamp,
                Source = payload.Source,
                Match = payload.Match,
                Confidence = (decimal)payload.Confidence,
                OffsetSeconds = (decimal)payload.OffsetSeconds,
                StreamElapsedSeconds = (decimal)payload.StreamElapsedSeconds,
                MasterDuration = (decimal)payload.MasterDuration,
                EvidenceFile = payload.EvidenceFile
            };

            OnNewResult?.Invoke(result);

            if (result.Match)
            {
                // Disparar alerta en segundo plano
                _ = Task.Run(async () => {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Sentinel.Dashboard.Data.ApplicationDbContext>();
                    
                    var station = await db.RadioStations.FirstOrDefaultAsync(r => r.StreamUrl == result.Source);
                    var audio = await db.MasterAudios.FirstOrDefaultAsync(a => a.LocalPath != null && result.Source.Contains(a.LocalPath)); 
                    // Nota: El audio es más difícil de encontrar sin el ID, pero podemos intentar por el path del master registrado.
                    // Por ahora, buscaremos el audio más reciente o el que coincida con el título si lo tuviéramos.
                    // Simplificación: Buscamos el audio que tiene el path que se usó para registrar.
                    
                    if (station != null)
                    {
                        // Intentar encontrar el audio por el path que el orchestrator conoce (en un escenario real pasaríamos el ID)
                        var masterAudio = await db.MasterAudios.OrderByDescending(a => a.Id).FirstOrDefaultAsync(); // Placeholder
                        await _alertService.NotifyDetectionAsync(station, masterAudio ?? new Sentinel.Dashboard.Models.Data.MasterAudio { Title = "Audio Desconocido" }, (double)result.Confidence);
                    }
                });
            }
        };
    }

    public bool IsRunning(string streamUrl) => _activeWorkers.ContainsKey(streamUrl);
    public bool AnyRunning => _activeWorkers.Count > 0;

    public void StartMonitor(string masterPath, string streamUrl)
    {
        if (IsRunning(streamUrl)) StopMonitor(streamUrl);

        try
        {
            var workerDir = Environment.GetEnvironmentVariable("EVIDENCE_DIR");
            if (string.IsNullOrEmpty(workerDir))
            {
                var execDir = AppContext.BaseDirectory;
                workerDir = Path.GetFullPath(Path.Combine(execDir, @"..\..\..\..\Sentinel.Worker\evidencia"));
            }
            if(!Directory.Exists(workerDir)) Directory.CreateDirectory(workerDir);

            var cts = new CancellationTokenSource();
            _activeWorkers[streamUrl] = cts;

            // Start processing natively in Background Task
            _ = Task.Run(async () => {
                await _audioMonitor.RegisterMasterTrackAsync(streamUrl, masterPath);
                await _audioMonitor.MonitorStreamAsync(streamUrl, workerDir, cts.Token);
            }, cts.Token).ContinueWith(t => {
                _activeWorkers.Remove(streamUrl);
                OnStatusChanged?.Invoke();
            });

            OnStatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Error iniciando supervisor nativo: {ex.Message}", streamUrl);
        }
    }

    public void StopMonitor(string streamUrl)
    {
        if (_activeWorkers.TryGetValue(streamUrl, out var cts))
        {
            try
            {
                cts.Cancel();
                _audioMonitor.UnregisterMaster(streamUrl);
                OnLog?.Invoke("[KERNEL] Terminado ciclo in-memory.", streamUrl);
            }
            catch { }
            _activeWorkers.Remove(streamUrl);
        }
        OnStatusChanged?.Invoke();
    }

    public void StopAll()
    {
        foreach (var url in _activeWorkers.Keys.ToList())
        {
            StopMonitor(url);
        }
    }

    public void Dispose()
    {
        StopAll();
    }
}
