using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Dashboard.Services;

/// <summary>
/// Servicio especializado en la orquestación de FFmpeg para procesamiento de audio.
/// Gestiona la lectura de streams, extracción de muestras PCM y codificación de evidencias.
/// </summary>
public class FFmpegAudioService
{
    private readonly string _ffmpegPath;

    /// <summary>
    /// Inicializa el servicio detectando la ubicación binaria de FFmpeg según el entorno (Windows/Docker).
    /// </summary>
    public FFmpegAudioService()
    {
        // Detectar path en Windows vs Docker
        _ffmpegPath = OperatingSystem.IsWindows() ? 
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Sentinel.Worker", "ffmpeg.exe") 
            : "ffmpeg";
            
        if (OperatingSystem.IsWindows() && !File.Exists(_ffmpegPath))
        {
            _ffmpegPath = "ffmpeg"; // Fallback al global si existe en PATH
        }
    }

    /// <summary>
    /// Lee un archivo o flujo de audio y extrae las muestras (samples) en formato float de 32 bits.
    /// Utiliza un sample rate específico (por defecto 5512Hz) optimizado para fingerprinting acústico.
    /// </summary>
    /// <param name="filePath">Ruta del archivo o URL del stream.</param>
    /// <param name="sampleRate">Frecuencia de muestreo objetivo.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación asíncrona.</param>
    /// <returns>Arreglo de floats conteniendo las muestras PCM decodificadas.</returns>
    public async Task<float[]> ReadAudioSamplesAsync(string filePath, int sampleRate = 5512, CancellationToken cancellationToken = default)
    {
        string tlsOpt = filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "-tls_verify 0" : "";
        
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-hide_banner -loglevel error {tlsOpt} -i \"{filePath}\" -f f32le -acodec pcm_f32le -ar {sampleRate} -ac 1 -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var memoryStream = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(memoryStream, cancellationToken);
        var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(copyTask, errTask);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var err = errTask.Result;
            throw new Exception($"FFmpeg extraction failed: {err}");
        }

        byte[] rawBytes = memoryStream.ToArray();
        
        // Convert f32le bytes to float array (Little Endian)
        float[] samples = new float[rawBytes.Length / 4];
        Buffer.BlockCopy(rawBytes, 0, samples, 0, rawBytes.Length);

        return samples;
    }

    /// <summary>
    /// Inicia un proceso FFmpeg para capturar un flujo de audio en tiempo real (HLS/Icecast)
    /// y lo expone a través de un StandardOutput para ser procesado por el kernel de audio.
    /// </summary>
    /// <param name="url">URL de la radio o stream.</param>
    /// <param name="sampleRate">Frecuencia de muestreo.</param>
    /// <returns>Instancia del proceso FFmpeg en ejecución.</returns>
    public Process GetStreamProcess(string url, int sampleRate = 5512)
    {
        string tlsOpt = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "-tls_verify 0" : "";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-hide_banner -loglevel error {tlsOpt} -re -i \"{url}\" -f f32le -acodec pcm_f32le -ar {sampleRate} -ac 1 -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    /// <summary>
    /// Codifica un arreglo de muestras PCM (floats) directamente a un archivo MP3 utilizando tuberías (pipes).
    /// Este método es utilizado para generar archivos de evidencia con post-roll tras una detección.
    /// </summary>
    /// <param name="samples">Muestras de audio capturadas.</param>
    /// <param name="sampleRate">Frecuencia de muestreo original.</param>
    /// <param name="outputPath">Ruta de destino del archivo MP3.</param>
    /// <returns>Ruta absoluta de la evidencia generada.</returns>
    public async Task<string> SaveEvidenceAsync(float[] samples, int sampleRate, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-hide_banner -loglevel error -f f32le -ar {sampleRate} -ac 1 -i pipe:0 -codec:a libmp3lame -qscale:a 2 \"{outputPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        byte[] rawBytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, rawBytes, 0, rawBytes.Length);

        using (var stdin = process.StandardInput.BaseStream)
        {
            await stdin.WriteAsync(rawBytes, 0, rawBytes.Length);
            await stdin.FlushAsync();
        }

        await process.WaitForExitAsync();
        return outputPath;
    }
}
