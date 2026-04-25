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
        int bufferSeconds = 120; // Guardamos los últimos 120 segundos en RAM por seguridad (comerciales largos + contexto)
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
            byte[] leftover = new byte[4];
            int leftoverCount = 0;

            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                int read = await baseStream.ReadAsync(readChunk, 0, bytesPerRead, ct);
                if (read == 0) break;

                // ALINEACIÓN DE DISPOSITIVO: Asegurar que siempre procesamos múltiplos de 4 bytes (floats)
                int totalAvailable = read + leftoverCount;
                int processableBytes = (totalAvailable / 4) * 4;
                int newLeftoverCount = totalAvailable % 4;

                byte[] alignedData = new byte[processableBytes];
                if (leftoverCount > 0)
                {
                    Buffer.BlockCopy(leftover, 0, alignedData, 0, leftoverCount);
                }
                Buffer.BlockCopy(readChunk, 0, alignedData, leftoverCount, processableBytes - leftoverCount);

                // Guardar sobrante para la siguiente lectura
                if (newLeftoverCount > 0)
                {
                    Buffer.BlockCopy(readChunk, read - newLeftoverCount, leftover, 0, newLeftoverCount);
                }
                leftoverCount = newLeftoverCount;

                totalSamplesProcessed += processableBytes / 4;

                // Implementación de Búfer Circular en RAM (Deslizante)
                if (currentBytes + processableBytes > maxBufferBytes)
                {
                    int overflow = (currentBytes + processableBytes) - maxBufferBytes;
                    Buffer.BlockCopy(buffer, overflow, buffer, 0, maxBufferBytes - overflow);
                    currentBytes -= overflow;
                }
                
                Buffer.BlockCopy(alignedData, 0, buffer, currentBytes, processableBytes);
                currentBytes += processableBytes;

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
                        
                        string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                        var trackD = _registeredMasters.GetValueOrDefault(streamUrl);
                        string radioName = trackD?.Track?.Title ?? "Desconocido"; // Usamos el ID o Título de la pista maestra registrada como nombre de carpeta
                        
                        string radioPath = Path.Combine(evidenceDir, radioName, dateFolder);
                        if (!Directory.Exists(radioPath)) Directory.CreateDirectory(radioPath);

                        string dateStr = DateTime.Now.ToString("HHmmss");
                        string fileName = $"testigo_{dateStr}.mp3";
                        string outPath = Path.Combine(radioPath, fileName);
                        
                        // Guardamos la ruta relativa para la DB
                        string dbRelativePath = Path.Combine(radioName, dateFolder, fileName);

                        await _audioSvc.SaveEvidenceAsync(evSamples, 5512, outPath);

                        OnTelemetry?.Invoke(streamUrl, $"[EVIDENCIA] Guardado: {dbRelativePath}");
                        
                        // Orquestación de Persistencia y Notificación
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
                                EvidenceFile = dbRelativePath
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
                                        EvidenceFileName = dbRelativePath
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

                // Ciclo de Análisis Acústico (Reacciona cada 10 segundos o cuando hay suficiente audio)
                var timeSinceLastAnalysis = (DateTime.Now - lastAnalysisTime).TotalSeconds;
                int minBytesToAnalyze = 5512 * 4 * 10; // Mínimo 10 segundos
                
                if (currentBytes >= minBytesToAnalyze && !capturingPostMatch && timeSinceLastAnalysis >= 10)
                {
                    lastAnalysisTime = DateTime.Now;
                    
                    // Solo analizamos lo que realmente tenemos acumulado (sube la fiabilidad al 100%)
                    float[] streamSamples = new float[currentBytes / 4];
                    Buffer.BlockCopy(buffer, 0, streamSamples, 0, currentBytes);

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
                        
                        // Configuración de ventana de grabación (Pre-roll de 7s + duración + 7s post)
                        double preRollSeconds = 7.0;
                        double startPointInCurrentBuffer = match.Audio.QueryMatchStartsAt - preRollSeconds;
                        if (startPointInCurrentBuffer < 0) startPointInCurrentBuffer = 0;
                        int startBytesOffset = (int)(startPointInCurrentBuffer * 5512) * 4;
                        int bytesToCopy = currentBytes - startBytesOffset;
                        
                        double alreadyCaptured = bytesToCopy / (5512.0 * 4);
                        double totalTarget = preRollSeconds + masterDuration + 7.0;
                        postMatchBytesRemaining = (long)((totalTarget - alreadyCaptured) * 5512 * 4);

                        byte[] winCopy = new byte[bytesToCopy];
                        Buffer.BlockCopy(buffer, startBytesOffset, winCopy, 0, bytesToCopy);
                        evidenceBuffer.SetLength(0);
                        await evidenceBuffer.WriteAsync(winCopy, 0, bytesToCopy);
                        // ELIMINADO: currentBytes = 0; -> El búfer ahora es continuo para no perder el rastro del comercial
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
            // ÚLTIMO ESCANEO DE SEGURIDAD (Para archivos cortos o cierres abruptos)
            if (currentBytes >= (5512 * 4 * 5) && !capturingPostMatch) // Al menos 5 segundos para huella útil
            {
                OnTelemetry?.Invoke(streamUrl, "[KERNEL] Ejecutando escaneo final de seguridad...");
                try
                {
                    float[] streamSamples = new float[currentBytes / 4];
                    Buffer.BlockCopy(buffer, 0, streamSamples, 0, currentBytes);
                    var queryResult = await QueryCommandBuilder.Instance
                        .BuildQueryCommand()
                        .From(new AudioSamples(streamSamples, streamUrl, 5512))
                        .UsingServices(_modelService)
                        .Query();

                    if (queryResult.BestMatch != null)
                    {
                        var match = queryResult.BestMatch;
                        OnTelemetry?.Invoke(streamUrl, $"[MATCH FINAL] Identificado en el cierre ({match.Audio.Confidence:P0}).");
                        
                        var trackD = _registeredMasters.GetValueOrDefault(streamUrl);
                        var payload = new TelemetryPayload {
                            Timestamp = DateTime.Now.ToString("o"), Source = streamUrl, Match = true,
                            Confidence = match.Audio.Confidence, OffsetSeconds = match.Audio.TrackMatchStartsAt,
                            StreamElapsedSeconds = (totalSamplesProcessed - (currentBytes / 4)) / 5512.0 + match.Audio.QueryMatchStartsAt,
                            MasterDuration = trackD?.Duration ?? 30.0, EvidenceFile = "final_scan.mp3"
                        };
                        OnMatchFound?.Invoke(payload);
                    }
                }
                catch { }
            }

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
