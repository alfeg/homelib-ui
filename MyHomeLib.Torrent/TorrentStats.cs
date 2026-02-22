namespace MyHomeListServer.Torrent;

/// <summary>Snapshot of TorrServe connection and library torrent state.</summary>
public record TorrentStats(
    bool   IsConnected,
    string State,               // e.g. "Torrent working", "In DB", "TorrServe unreachable"
    // Transfer
    double DownloadSpeed      = 0,   // bytes/s
    double UploadSpeed        = 0,   // bytes/s
    // Peers
    int    TotalPeers         = 0,
    int    PendingPeers       = 0,
    int    ActivePeers        = 0,
    int    HalfOpenPeers      = 0,
    int    ConnectedSeeders   = 0,
    // Cache / Preload
    long   LoadedSize         = 0,   // bytes downloaded so far
    long   TorrentSize        = 0,   // total torrent size in bytes
    long   PreloadedBytes     = 0,
    long   PreloadSize        = 0,
    long   CacheCapacity      = 0,   // TorrServe cache capacity
    long   CacheFilled        = 0,   // TorrServe cache used
    int    CachedPieces       = 0,   // number of cached pieces
    int    ActiveReaders      = 0,   // active stream readers
    // I/O counters
    long   BytesWritten       = 0,
    long   BytesRead          = 0,
    long   BytesReadUseful    = 0,
    long   ChunksRead         = 0,
    long   ChunksReadUseful   = 0,
    long   ChunksReadWasted   = 0,
    long   PiecesDirtiedGood  = 0,
    long   PiecesDirtiedBad   = 0,
    // Misc
    double DurationSeconds    = 0,
    string BitRate            = "",
    string TorrServVersion    = ""   // from /echo
);
