namespace MyHomeListServer.Torrent;

public class AppConfig
{
    public string CacheDirectory { get; set; } = "./cache";
    public List<string> AnnounceUrls { get; set; } = new List<string>();
    public List<string> SpecialPeers { get; set; } = new List<string>();
    /// <summary>UDP/TCP listen port for DHT and peer connections. Default 6881.</summary>
    public int ListenPort { get; set; } = 6881;
    /// <summary>
    /// When set, TorrServe is used for torrent streaming instead of the built-in MonoTorrent engine.
    /// Example: "http://localhost:8090"
    /// </summary>
    public string TorrServeUrl { get; set; } = "";
}