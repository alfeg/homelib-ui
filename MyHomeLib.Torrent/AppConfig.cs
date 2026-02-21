namespace MyHomeListServer.Torrent;

public class AppConfig
{
    public string CacheDirectory { get; set; } = "./cache";
    public List<string> AnnounceUrls { get; set; } = new List<string>();
    public List<string> SpecialPeers { get; set; } = new List<string>();
    /// <summary>UDP/TCP listen port for DHT and peer connections. Default 6881.</summary>
    public int ListenPort { get; set; } = 6881;
}