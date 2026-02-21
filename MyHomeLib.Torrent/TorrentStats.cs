namespace MyHomeListServer.Torrent;

/// <summary>Live snapshot of a torrent manager during download.</summary>
public record TorrentStats(
    double DownloadRateBytesPerSec,
    double UploadRateBytesPerSec,
    long BytesReceived,
    int Seeds,
    int Peers,
    double PartialProgress   // 0 – 100
);
