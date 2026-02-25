namespace MyHomeLib.Torrent;

public record DownloadRequest(string Library, string Archive, string? Book = null)
    : TorrentRequest(Library)
{
    public string Name => $"{Archive} => {Book}";
    public override string ToString() => $"DownloadRequest [{Library}] {Name}";
}
