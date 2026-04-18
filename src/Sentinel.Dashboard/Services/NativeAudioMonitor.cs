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

public class NativeAudioMonitor
{
    private readonly FFmpegAudioService _audioSvc;
    private readonly IModelService _modelService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, TrackData> _registeredMasters = new();

    // Evento para emitir telemetría al UI
    public event Action<string, string> OnTelemetry;
    public event Action<TelemetryPayload> OnMatchFound;

    public NativeAudioMonitor(FFmpegAudioService audioSvc, IServiceScopeFactory scopeFactory)
    {
        _audioSvc = audioSvc;
        _scopeFactory = scopeFactory;
        _modelService = new InMemoryModelService();
    }

    /// <summary>
    /// Calcula e inserta un MP3 maestro a la base de datos acústica en memoria.
    /// </summary>
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
            
            OnTelemetry?.Invoke(radioUrl, $"[C# CORE] Matriz Maestra encriptada. {hash.Count} firmas acústicas.");
        }
        catch (Exception ex)
        {
            OnTelemetry?.Invoke(radioUrl, $"[C# FATAL] Error al generar huella maestra: {ex.Message}");
        }
    }

    /// <summary>
    /// Remueve la huella maestra si se detiene la vinculación
    /// </summary>
    public void UnregisterMaster(string radioUrl)
    {
        if (_registeredMasters.TryRemove(radioUrl, out var trackData))
        {
            _modelService.DeleteTrack(trackData.Track.Id);
        }
    }

    /// <summary>
    /// Loop infinito de monitorización (reemplaza al monitor.py y fpcalc.exe)
    /// </summary>
    public async Task MonitorStreamAsync(string streamUrl, string evidenceDir, CancellationToken ct)
    {
        string safeUrl = streamUrl.Replace("\"", "").Trim(); // Permite espacios en rutas locales
        
        OnTelemetry?.Invoke(streamUrl, $"[C# CORE] Enganchando tuner FFMPEG: {safeUrl}");

        int bytesPerRead = 5512 * 4 * 2; // ~2 segundos de Float32 a 5512Hz
        int bufferSeconds = 45;
        int maxBufferBytes = 5512 * 4 * bufferSeconds;
        
        using var process = _audioSvc.GetStreamProcess(safeUrl, 5512);
        var baseStream = process.StandardOutput.BaseStream;
        byte[] buffer = new byte[maxBufferBytes];
        int currentBytes = 0;
        
        // Post-match logic
        bool capturingPostMatch = false;
        long postMatchBytesRemaining = 0;
        MemoryStream evidenceBuffer = new MemoryStream();
        
        long totalSamplesProcessed = 0;
        double lastMatchStreamElapsed = 0;
        double lastMatchOffset = 0;
        double lastMatchConfidence = 0;
        DateTime lastAnalysisTime = DateTime.MinValue;

        try
        {
            byte[] readChunk = new byte[bytesPerRead];
            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                int read = await baseStream.ReadAsync(readChunk, 0, bytesPerRead, ct);
                if (read == 0) 
                {
                    if (process.HasExited && process.ExitCode != 0)
                    {
                        var errStr = await process.StandardError.ReadToEndAsync();
                        OnTelemetry?.Invoke(streamUrl, $"[C# FFMPEG FATAL] {errStr}");
                    }
                    break;
                }
                
                totalSamplesProcessed += read / 4;

                // Rotar Búfer circular si se llena o añadir normal
                if (currentBytes + read > maxBufferBytes)
                {
                    int overflow = (currentBytes + read) - maxBufferBytes;
                    Buffer.BlockCopy(buffer, overflow, buffer, 0, maxBufferBytes - overflow);
                    currentBytes -= overflow;
                }
                
                Buffer.BlockCopy(readChunk, 0, buffer, currentBytes, read);
                currentBytes += read;

                if (capturingPostMatch)
                {
                    await evidenceBuffer.WriteAsync(readChunk, 0, read);
                    postMatchBytesRemaining -= read;

                    if (postMatchBytesRemaining <= 0)
                    {
                        // Finalizar y Guardar la evidencia con los márgenes perfectos
                        capturingPostMatch = false;
                        byte[] finalEvidenceBytes = evidenceBuffer.ToArray();
                        int validBytesCount = (finalEvidenceBytes.Length / 4) * 4;
                        float[] evSamples = new float[validBytesCount / 4];
                        Buffer.BlockCopy(finalEvidenceBytes, 0, evSamples, 0, validBytesCount);
                        
                        string dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string outPath = Path.Combine(evidenceDir, $"match_csharp_{dateStr}.mp3");
                        await _audioSvc.SaveEvidenceAsync(evSamples, 5512, outPath);

                        OnTelemetry?.Invoke(streamUrl, $"[EVIDENCIA C#] Post-roll grabado ({evSamples.Length / 5512f:F1}s). Archivo: {Path.GetFileName(outPath)}");
                        
                        // Notificar el Match completo subiendo Telemetría
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

                            // --- PERSISTENCIA POSTGRESQL (ENTERPRISE) ---
                            _ = Task.Run(async () => {
                                try {
                                    using var scope = _scopeFactory.CreateScope();
                                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                                    // Asegurar que la Radio existe
                                    var radio = await db.RadioStations.FirstOrDefaultAsync(r => r.StreamUrl == streamUrl);
                                    if (radio == null) {
                                        radio = new RadioStation { 
                                            Name = $"Radio {Path.GetFileNameWithoutExtension(streamUrl)}", 
                                            StreamUrl = streamUrl 
                                        };
                                        db.RadioStations.Add(radio);
                                        await db.SaveChangesAsync();
                                    }

                                    // Asegurar que el Master existe (usamos el Title como referencia única por ahora)
                                    var master = await db.MasterAudios.FirstOrDefaultAsync(m => m.Title == trackD.Track.Title);
                                    if (master == null) {
                                        master = new MasterAudio { 
                                            Title = trackD.Track.Title, 
                                            Duration = trackD.Duration,
                                            LocalPath = trackD.Track.Id // Guardamos el ID de SF como path de referencia
                                        };
                                        db.MasterAudios.Add(master);
                                        await db.SaveChangesAsync();
                                    }

                                    // Guardar el Registro de Auditoría
                                    var record = new MatchRecord {
                                        DetectionTime = DateTime.UtcNow,
                                        RadioStationId = radio.Id,
                                        MasterAudioId = master.Id,
                                        Confidence = lastMatchConfidence,
                                        MatchOffsetSeconds = lastMatchOffset,
                                        StreamElapsedSeconds = lastMatchStreamElapsed,
                                        EvidenceFileName = Path.GetFileName(outPath)
                                    };
                                    db.MatchRecords.Add(record);
                                    await db.SaveChangesAsync();
                                } catch (Exception ex) {
                                    // Logueamos pero NO lanzamos el error para no matar el Thread Pool ni al padre
                                    Console.WriteLine($"[DB ERROR CRÍTICO] Fallo en persistencia: {ex.Message}");
                                    OnTelemetry?.Invoke(streamUrl, $"[DB ERROR] No se pudo persistir match: {ex.Message}");
                                }
                            });
                        }
                        
                        evidenceBuffer.SetLength(0);
                        continue;
                    }
                }

                // Analizar cada ~10 segundos si tenemos el búfer lleno y no estamos capturando el post-match
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
                        double masterDuration = trackD != null ? trackD.Duration : 30.0;
                        
                        // Queremos guardar 5 segundos de pre-roll exactos
                        double preRollSeconds = 5.0;
                        double startPointInCurrentBuffer = match.Audio.QueryMatchStartsAt - preRollSeconds;
                        if (startPointInCurrentBuffer < 0) startPointInCurrentBuffer = 0; // Si la radio empezó hace menos de 5 seg
                        int startSamplesOffset = (int)(startPointInCurrentBuffer * 5512);
                        int startBytesOffset = startSamplesOffset * 4;
                        if (startBytesOffset > currentBytes) startBytesOffset = currentBytes;
                        int bytesToCopy = currentBytes - startBytesOffset;
                        if (bytesToCopy < 0) bytesToCopy = 0;
                        
                        // ¿Cuánto tiempo extra falta escuchar para grabar el final del comercial + 5 segundos post?
                        double audioAlreadyInCopiedBufferSeconds = bytesToCopy / (5512.0 * 4);
                        double targetTotalSecondsToCapture = preRollSeconds + masterDuration + 5.0;
                        double remainingSecondsToCapture = targetTotalSecondsToCapture - audioAlreadyInCopiedBufferSeconds;
                        if (remainingSecondsToCapture < 0) remainingSecondsToCapture = 0;
                        
                        postMatchBytesRemaining = (long)(remainingSecondsToCapture * 5512 * 4);
                        if (postMatchBytesRemaining < 1) postMatchBytesRemaining = 1; // Mínimo un byte para entrar al loop

                        // El motor C# lo detectó estelarmente.
                        OnTelemetry?.Invoke(streamUrl, $"[C# KERNEL] Comercial Hit! Recortando 5s de pre-roll. Gravando {remainingSecondsToCapture:F1}s adicionales...");
                        capturingPostMatch = true;
                        
                        // Recortamos la ventana precisa y la pasamos al grabador final
                        byte[] winCopy = new byte[bytesToCopy];
                        Buffer.BlockCopy(buffer, startBytesOffset, winCopy, 0, bytesToCopy);
                        evidenceBuffer.SetLength(0);
                        await evidenceBuffer.WriteAsync(winCopy, 0, bytesToCopy);
                        
                        // Limpiar búfer
                        currentBytes = 0;
                    }
                    else
                    {
                        var score = queryResult.ResultEntries.FirstOrDefault()?.Audio?.Confidence ?? 0.0;
                        OnTelemetry?.Invoke(streamUrl, $"Silencio o no-match. Confidence Actual = {score:P2}");
                    }
                    // Pequeña pausa para no enloquecer si caemos en el segundo 10 muchas veces
                    await Task.Delay(1000, ct); 
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnTelemetry?.Invoke(streamUrl, $"[FATAL] Conexión abortada en Core C#: {ex.Message}");
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
