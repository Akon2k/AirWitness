using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Dashboard.Services;

public class FFmpegAudioService
{
    private readonly string _ffmpegPath;

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
    /// Extrae todos los samples de un archivo/stream local en floats de 32 bits, 5512hz, Mono
    /// </summary>
    public async Task<float[]> ReadAudioSamplesAsync(string filePath, int sampleRate = 5512, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-hide_banner -loglevel error -i \"{filePath}\" -f f32le -acodec pcm_f32le -ar {sampleRate} -ac 1 -",
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
        
        // Convert f32le bytes to float array
        float[] samples = new float[rawBytes.Length / 4];
        Buffer.BlockCopy(rawBytes, 0, samples, 0, rawBytes.Length);

        return samples;
    }

    /// <summary>
    /// Retorna un flujo continuo desde un Stream de internet (Process + BaseStream)
    /// </summary>
    public Process GetStreamProcess(string url, int sampleRate = 5512)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-hide_banner -loglevel error -re -i \"{url}\" -f f32le -acodec pcm_f32le -ar {sampleRate} -ac 1 -",
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
    /// Permite guardar un buffer PCM temporal directamente a MP3 guardando 7 segundos de postroll.
    /// Como el buffer de SFingerprinting ya es Float[], lo empujamos desde C# a FFMPEG por stdin.
    /// </summary>
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
