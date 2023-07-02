namespace MyHomeLib.Files.Core;

public class AppConfig
{
    public string CacheDirectory { get; set; } = "./cache";
    public List<string> AnnounceUrls { get; set; } = new List<string>();
    public List<string> SpecialPeers { get; set; } = new List<string>();
}