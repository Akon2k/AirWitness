using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Data;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Dashboard.Data;
using Sentinel.Dashboard.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace Sentinel.Dashboard.Services;

/// <summary>
/// El "Núcleo Nuclear" de AirWitness. Procesa el audio en tiempo real usando SoundFingerprinting.
/// Reemplaza la necesidad de scripts externos (como monitor.py) ejecutando todo el proceso en la memoria de la JVM/.NET.
/// </summary>
public class NativeAudioMonitor
{
    private readonly FFmpegAudioService _audioSvc;
    private readonly IModelService _modelService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, TrackData> _registeredMasters = new();

    /// <summary>
    /// Evento disparado para telemetría visual en el Dashboard (logs de consola).
    /// </summary>
    public event Action<string, string> OnTelemetry;
    
    /// <summary>
    /// Evento disparado cuando se confirma un 'Hit' acústico positivo.
    /// </summary>
    public event Action<TelemetryPayload> OnMatchFound;

    public NativeAudioMonitor(FFmpegAudioService audioSvc, IServiceScopeFactory scopeFactory)
    {
        _audioSvc = audioSvc;
        _scopeFactory = scopeFactory;
        _modelService = new InMemoryModelService(); // Base de datos acústica volátil de alto rendimiento
    }

    /// <summary>
    /// Registra una pista maestra (comercial) en el motor de búsqueda acústica.
    /// Genera los 'sub-fingerprints' necesarios para la comparación en tiempo real.
    /// </summary>
    /// <param name="radioUrl">ID vinculante de la radio.</param>
    /// <param name="mp3Path">Ruta física del archivo comercial.</param>
    public async Task RegisterMasterTrackAsync(string radioUrl, string mp3Path)
    {
        if (string.IsNullOrEmpty(mp3Path) || !File.Exists(mp3Path)) return;

        try
        {
            float[] samples = await _audioSvc.ReadAudioSamplesAsync(mp3Path, 5512);
            var audioSamples = new AudioSamples(samples, radioUrl, 5512);

            var hash = await FingerprintCommandBuilder.Instance
                .BuildFingerprintCommand()
                .From(audioSamples)
                .Hash();

            var trackInfo = new TrackInfo(radioUrl, "Comercial", radioUrl);
            _modelService.Insert(trackInfo, hash);
            _registeredMasters[radioUrl] = new TrackData { Track = trackInfo, Duration = (float)samples.Length / 5512f };
            
            OnTelemetry?.Invoke(radioUrl, $"[KERNEL] Matriz Maestra registrada: {hash.Count} firmas digitales.");
        }
        catch (Exception ex)
        {
            OnTelemetry?.Invoke(radioUrl, $"[ERROR KERNEL] Fallo al generar huella: {ex.Message}");
        }
    }

    /// <summary>
    /// Remueve la huella maestra de la memoria RAM cuando se detiene el monitoreo.
    /// </summary>
    public void UnregisterMaster(string radioUrl)
    {
        if (_registeredMasters.TryRemove(radioUrl, out var trackData))
        {
            _modelService.DeleteTrack(trackData.Track.Id);
        }
    }

    /// <summary>
    /// Bucle principal de monitoreo en tiempo real.
    /// Captura audio del stream, lo procesa en un búfer circular y busca coincidencias cada 10 segundos.
    /// </summary>
    /// <param name="streamUrl">URL origen del audio.</param>
    /// <param name="evidenceDir">Carpeta de destino para grabaciones de evidencia.</param>
    /// <param name="ct">Token para detener el proceso limpiamente.</param>
    public async Task MonitorStreamAsync(string streamUrl, string evidenceDir, CancellationToken ct)
    {
        string safeUrl = streamUrl.Replace("\"", "").Trim();
        
        OnTelemetry?.Invoke(streamUrl, $"[KERNEL] Iniciando tuner digital: {safeUrl}");

        int bytesPerRead = 5512 * 4 * 2; // ~2 segundos de Float32 a 5512Hz
        int bufferSeconds = 45; // Guardamos los últimos 45 segundos en RAM para contexto
        int maxBufferBytes = 5512 * 4 * bufferSeconds;
        
        using var process = _audioSvc.GetStreamProcess(safeUrl, 5512);
        var baseStream = process.StandardOutput.BaseStream;
        byte[] buffer = new byte[maxBufferBytes];
        int currentBytes = 0;
        
        long totalSamplesProcessed = 0;
        double lastMatchStreamElapsed = 0;
        double lastMatchOffset = 0;
        double lastMatchConfidence = 0;
        DateTime lastAnalysisTime = DateTime.MinValue;

        // Estado para grabación de evidencia post-detección
        bool capturingPostMatch = false;
        long postMatchBytesRemaining = 0;
        using MemoryStream evidenceBuffer = new MemoryStream();

        try
        {
            byte[] readChunk = new byte[bytesPerRead];
            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                int read = await baseStream.ReadAsync(readChunk, 0, bytesPerRead, ct);
                if (read == 0) break;
                
                totalSamplesProcessed += read / 4;

                // Implementación de Búfer Circular en RAM
                if (currentBytes + read > maxBufferBytes)
                {
                    int overflow = (currentBytes + read) - maxBufferBytes;
                    Buffer.BlockCopy(buffer, overflow, buffer, 0, maxBufferBytes - overflow);
                    currentBytes -= overflow;
                }
                
                Buffer.BlockCopy(readChunk, 0, buffer, currentBytes, read);
                currentBytes += read;

                // Grabación de Evidencia (Post-Match)
                if (capturingPostMatch)
                {
                    await evidenceBuffer.WriteAsync(readChunk, 0, read);
                    postMatchBytesRemaining -= read;

                    if (postMatchBytesRemaining <= 0)
                    {
                        capturingPostMatch = false;
                        byte[] finalEvidenceBytes = evidenceBuffer.ToArray();
                        int validBytesCount = (finalEvidenceBytes.Length / 4) * 4;
                        float[] evSamples = new float[validBytesCount / 4];
                        Buffer.BlockCopy(finalEvidenceBytes, 0, evSamples, 0, validBytesCount);
                        
                        string dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string outPath = Path.Combine(evidenceDir, $"match_csharp_{dateStr}.mp3");
                        await _audioSvc.SaveEvidenceAsync(evSamples, 5512, outPath);

                        OnTelemetry?.Invoke(streamUrl, $"[EVIDENCIA] Grabación finalizada: {Path.GetFileName(outPath)}");
                        
                        // Orquestación de Persistencia y Notificación
                        var trackD = _registeredMasters.GetValueOrDefault(streamUrl);
                        if (trackD != null)
                        {
                            var payload = new TelemetryPayload
                            {
                                Timestamp = DateTime.Now.ToString("o"),
                                Source = streamUrl,
                                Match = true,
                                Confidence = lastMatchConfidence,
                                OffsetSeconds = lastMatchOffset,
                                StreamElapsedSeconds = lastMatchStreamElapsed,
                                MasterDuration = trackD.Duration,
                                EvidenceFile = Path.GetFileName(outPath)
                            };

                            OnMatchFound?.Invoke(payload);

                            // Registro asíncrono en PostgreSQL (Enterprise Layer)
                            _ = Task.Run(async () => {
                                try {
                                    using var scope = _scopeFactory.CreateScope();
                                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                                    var radio = await db.RadioStations.FirstOrDefaultAsync(r => r.StreamUrl == streamUrl);
                                    if (radio == null) {
                                        radio = new RadioStation { Name = "Radio Auto-Detect", StreamUrl = streamUrl };
                                        db.RadioStations.Add(radio);
                                        await db.SaveChangesAsync();
                                    }

                                    var master = await db.MasterAudios.FirstOrDefaultAsync(m => m.Title == trackD.Track.Title);
                                    if (master == null) {
                                        master = new MasterAudio { Title = trackD.Track.Title, Duration = trackD.Duration };
                                        db.MasterAudios.Add(master);
                                        await db.SaveChangesAsync();
                                    }

                                    db.MatchRecords.Add(new MatchRecord {
                                        DetectionTime = DateTime.UtcNow,
                                        RadioStationId = radio.Id,
                                        MasterAudioId = master.Id,
                                        Confidence = lastMatchConfidence,
                                        MatchOffsetSeconds = lastMatchOffset,
                                        EvidenceFileName = Path.GetFileName(outPath)
                                    });
                                    await db.SaveChangesAsync();
                                } catch (Exception ex) {
                                    Console.WriteLine($"[DB FAIL] {ex.Message}");
                                }
                            });
                        }
                        evidenceBuffer.SetLength(0);
                    }
                }

                // Ciclo de Análisis Acústico (cada 10 segundos)
                var timeSinceLastAnalysis = (DateTime.Now - lastAnalysisTime).TotalSeconds;
                if (currentBytes >= maxBufferBytes && !capturingPostMatch && timeSinceLastAnalysis >= 10)
                {
                    lastAnalysisTime = DateTime.Now;
                    float[] streamSamples = new float[maxBufferBytes / 4];
                    Buffer.BlockCopy(buffer, 0, streamSamples, 0, maxBufferBytes);

                    var queryResult = await QueryCommandBuilder.Instance
                        .BuildQueryCommand()
                        .From(new AudioSamples(streamSamples, streamUrl, 5512))
                        .UsingServices(_modelService)
                        .Query();

                    if (queryResult.BestMatch != null)
                    {
                        var match = queryResult.BestMatch;
                        lastMatchConfidence = match.Audio.Confidence;
                        lastMatchOffset = match.Audio.TrackMatchStartsAt;
                        double bufferStartTime = (totalSamplesProcessed - (currentBytes / 4)) / 5512.0;
                        lastMatchStreamElapsed = bufferStartTime + match.Audio.QueryMatchStartsAt;

                        var trackD = _registeredMasters.GetValueOrDefault(streamUrl);
                        double masterDuration = trackD?.Duration ?? 30.0;
                        
                        // Configuración de ventana de grabación (Pre-roll de 5s + duración + 5s post)
                        double preRollSeconds = 5.0;
                        double startPointInCurrentBuffer = match.Audio.QueryMatchStartsAt - preRollSeconds;
                        if (startPointInCurrentBuffer < 0) startPointInCurrentBuffer = 0;
                        int startBytesOffset = (int)(startPointInCurrentBuffer * 5512) * 4;
                        int bytesToCopy = currentBytes - startBytesOffset;
                        
                        double alreadyCaptured = bytesToCopy / (5512.0 * 4);
                        double totalTarget = preRollSeconds + masterDuration + 5.0;
                        postMatchBytesRemaining = (long)((totalTarget - alreadyCaptured) * 5512 * 4);

                        OnTelemetry?.Invoke(streamUrl, $"[IDENTIFICADO] Coincidencia encontrada ({lastMatchConfidence:P0}). Iniciando captura de evidencia.");
                        capturingPostMatch = true;
                        
                        byte[] winCopy = new byte[bytesToCopy];
                        Buffer.BlockCopy(buffer, startBytesOffset, winCopy, 0, bytesToCopy);
                        evidenceBuffer.SetLength(0);
                        await evidenceBuffer.WriteAsync(winCopy, 0, bytesToCopy);
                        currentBytes = 0;
                    }
                    else
                    {
                        var score = queryResult.ResultEntries.FirstOrDefault()?.Audio?.Confidence ?? 0.0;
                        OnTelemetry?.Invoke(streamUrl, $"[ESCANEO] Sin hallazgos. Confianza: {score:P2}");
                    }
                    await Task.Delay(1000, ct); 
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnTelemetry?.Invoke(streamUrl, $"[ERROR CRÍTICO] {ex.Message}");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
    }

    private class TrackData
    {
        public TrackInfo Track { get; set; }
        public float Duration { get; set; }
    }
}

/// <summary>
/// Modelo de datos para el transporte de resultados de detección en tiempo real.
/// </summary>
public class TelemetryPayload
{
    public string Timestamp { get; set; }
    public string Source { get; set; }
    public bool Match { get; set; }
    public double Confidence { get; set; }
    public double OffsetSeconds { get; set; }
    public double StreamElapsedSeconds { get; set; }
    public double MasterDuration { get; set; }
    public string EvidenceFile { get; set; }
}
