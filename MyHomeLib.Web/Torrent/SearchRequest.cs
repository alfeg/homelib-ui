namespace MyHomeLib.Web;

public record SearchRequest(string Library, string FilePattern) : TorrentRequest(Library);
