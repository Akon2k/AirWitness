using System.Text.RegularExpressions;
using System.Net.Http;

namespace Sentinel.Dashboard.Services;

public class StreamDiscoveryService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<StreamDiscoveryService> _logger;

    public StreamDiscoveryService(IHttpClientFactory clientFactory, ILogger<StreamDiscoveryService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<DiscoveryResult> DiscoverStreamAsync(string url)
    {
        var result = new DiscoveryResult { TargetUrl = url };
        
        try
        {
            var client = _clientFactory.CreateClient("Insecure");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var initialResponse = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!initialResponse.IsSuccessStatusCode)
            {
                result.Error = $"El servidor respondió con error: {initialResponse.StatusCode}";
                return result;
            }

            var contentType = initialResponse.Content.Headers.ContentType?.MediaType ?? "";
            
            // Si el link YA ES un stream directo, no intentamos descargar el HTML
            if (contentType.Contains("audio") || contentType.Contains("mpeg") || contentType.Contains("octet-stream"))
            {
                result.Streams.Add(new StreamCandidate { 
                    Url = url, 
                    Type = contentType.Split('/').Last(), 
                    Source = "Direct Link Detection",
                    IsWorking = true 
                });
                return result;
            }

            var response = await initialResponse.Content.ReadAsStringAsync();
            
            // 1. Buscar patrones comunes de streams (MP3, AAC, M3U8, etc)
            var streamRegex = new Regex(@"(https?://[^\s""'>]+\.(mp3|aac|m3u|m3u8|pls|wav|ogg)(?:\?[^\s""'>]*)?)", RegexOptions.IgnoreCase);
            var matches = streamRegex.Matches(response);
            
            foreach (Match match in matches)
            {
                result.Streams.Add(new StreamCandidate { Url = match.Value, Type = match.Groups[2].Value, Source = "Pattern Match" });
            }

            // 2. Buscar patrones de Icecast/Shoutcast (/stream, /live, ;stream.mp3)
            var icecastRegex = new Regex(@"(https?://[^\s""'>]+/(?:stream|live|listen|radio|broadcast)(?:\.mp3)?(?:\?[^\s""'>]*)?)", RegexOptions.IgnoreCase);
            var iceMatches = icecastRegex.Matches(response);
            foreach (Match match in iceMatches)
            {
                if (!result.Streams.Any(s => s.Url == match.Value))
                    result.Streams.Add(new StreamCandidate { Url = match.Value, Type = "icecast", Source = "Icecast Pattern" });
            }

            // 3. Patrones específicos de proveedores (Zeno.FM, SonicPanel)
            if (response.Contains("sonic.portalfoxmix.club") || response.Contains("sonicpanel"))
            {
                var sonicRegex = new Regex(@"(https?://[^\s""'>]+:\d+/stream)", RegexOptions.IgnoreCase);
                var sonicMatches = sonicRegex.Matches(response);
                foreach (Match match in sonicMatches)
                {
                    if (!result.Streams.Any(s => s.Url == match.Value))
                        result.Streams.Add(new StreamCandidate { Url = match.Value, Type = "sonic", Source = "SonicPanel Detector" });
                }
            }

            if (response.Contains("zeno.fm"))
            {
                 var zenoRegex = new Regex(@"(https?://stream\.zeno\.fm/[^\s""'>]+)", RegexOptions.IgnoreCase);
                 var zenoMatches = zenoRegex.Matches(response);
                 foreach (Match match in zenoMatches)
                 {
                    if (!result.Streams.Any(s => s.Url == match.Value))
                        result.Streams.Add(new StreamCandidate { Url = match.Value, Type = "zeno", Source = "Zeno.FM Detector" });
                 }
            }

            // 4. Buscar en etiquetas <audio> o <source>
            var audioTagRegex = new Regex(@"<(?:audio|source)[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            var tagMatches = audioTagRegex.Matches(response);
            foreach (Match match in tagMatches)
            {
                var src = match.Groups[1].Value;
                if (!src.StartsWith("http"))
                {
                    // Manejar URLs relativas
                    try {
                        var baseUri = new Uri(url);
                        src = new Uri(baseUri, src).ToString();
                    } catch {}
                }
                if (!result.Streams.Any(s => s.Url == src))
                    result.Streams.Add(new StreamCandidate { Url = src, Type = "html5", Source = "Audio Tag" });
            }

            // Validar los candidatos
            foreach (var stream in result.Streams)
            {
                stream.IsWorking = await ValidateStreamAsync(stream.Url);
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private async Task<bool> ValidateStreamAsync(string url)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler { 
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true 
            });
            client.Timeout = TimeSpan.FromSeconds(5);
            
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                var contentType = response.Content.Headers.ContentType?.MediaType;
                return contentType != null && (contentType.Contains("audio") || contentType.Contains("mpeg") || contentType.Contains("application/ogg") || contentType.Contains("video/mp2t"));
            }
        }
        catch { }
        return false;
    }
}

public class DiscoveryResult
{
    public string TargetUrl { get; set; } = "";
    public List<StreamCandidate> Streams { get; set; } = new();
    public string? Error { get; set; }
}

public class StreamCandidate
{
    public string Url { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsWorking { get; set; }
}
