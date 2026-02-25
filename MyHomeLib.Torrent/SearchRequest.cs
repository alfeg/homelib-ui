namespace MyHomeLib.Torrent;

public record SearchRequest(string Library, string FilePattern) : TorrentRequest(Library);
