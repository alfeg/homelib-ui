namespace MyHomeListServer.Torrent;

/// <summary>Live snapshot of a torrent manager during download.</summary>
public record TorrentStats(
    double DownloadRateBytesPerSec,
    double UploadRateBytesPerSec,
    long BytesReceived,
    int Seeds,
    int Peers,
    double PartialProgress,   // 0 – 100
    string State,             // TorrentState as string, e.g. "Metadata", "Downloading"
    int DhtNodes              // number of DHT nodes the engine knows about
);
