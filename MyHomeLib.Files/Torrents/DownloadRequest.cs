namespace MyHomeLib.Files.Torrents;

public record DownloadRequest(string Library, string Archive, string? Book = null) : TorrentRequest(Library)
{
    public override string ToString()
    {
        return $"[{Library}] {Name}";
    }

    public string Name => $"{Archive} => {Book}";
}
