using MonoTorrent;

namespace MyHomeListServer.Torrent;

public record TorrentRequest(string Library, bool RequireStartStop = true)
{
    public MagnetLink? Link { get; set; }
    public IProgress<TorrentStats>? Progress { get; set; }
};