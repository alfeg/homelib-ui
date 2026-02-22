using MonoTorrent;

namespace MyHomeListServer.Torrent;

public record TorrentRequest(string Library)
{
    public MagnetLink? Link { get; set; }
    public IProgress<TorrentStats>? Progress { get; set; }
};