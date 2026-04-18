namespace Sentinel.Dashboard.Services;

public class RadioTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Radio Nueva";
    public string StreamUrl { get; set; } = "";
    public string MasterPath { get; set; } = "";
    
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string Category { get; set; } = "Radio";
    
    // Propiedades de estado en tiempo real (no se guardan en JSON)
    public bool IsMonitoring { get; set; }
    public decimal BestConfidence { get; set; }
    public string LastLog { get; set; } = "Esperando...";
    public bool IsUploading { get; set; }
    public bool IsValidating { get; set; }
    public string LinkStatus { get; set; } = "unknown"; // unknown, online, offline
    public List<MatchResult> MatchHistory { get; set; } = new();

    // Propiedades de UI para control de audio local
    public bool IsListeningLive { get; set; }
    public bool IsListeningSample { get; set; }
}
