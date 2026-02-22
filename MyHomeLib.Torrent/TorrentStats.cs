namespace MyHomeListServer.Torrent;

/// <summary>Snapshot of TorrServe connection and library torrent state.</summary>
public record TorrentStats(
    bool   IsConnected,
    string State        // e.g. "Torrent working", "In DB", "TorrServe unreachable"
);
