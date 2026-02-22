using MonoTorrent;

namespace MyHomeListServer.Torrent;

public record SearchRequest(string Library, string FilePattern) : TorrentRequest(Library)
{
}
