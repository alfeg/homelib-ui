namespace MyHomeListServer.Torrent;

public record TorrentRequest(string Library)
{
    /// <summary>Full magnet URI. Required for AddTorrentAsync calls.</summary>
    public string? MagnetUri { get; set; }
    public IProgress<TorrentStats>? Progress { get; set; }
};