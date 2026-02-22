namespace MyHomeListServer.Torrent;

/// <summary>Snapshot of TorrServe connection and library torrent state.</summary>
public record TorrentStats(
    bool   IsConnected,
    string State,        // e.g. "Torrent working", "In DB", "TorrServe unreachable"
    double DownloadSpeed  = 0,   // bytes/s
    double UploadSpeed    = 0,   // bytes/s
    int    TotalPeers     = 0,
    int    ActivePeers    = 0,
    int    ConnectedSeeders = 0,
    long   LoadedSize     = 0,   // bytes downloaded so far
    long   TorrentSize    = 0    // total torrent size in bytes
);
