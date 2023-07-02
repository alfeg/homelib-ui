namespace MyHomeLib.Files.Torrents;

public record SearchRequest(string Library, string FilePattern) : TorrentRequest(Library)
{
}