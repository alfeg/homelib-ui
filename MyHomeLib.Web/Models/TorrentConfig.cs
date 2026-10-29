namespace MyHomeLib.Web.Models;

public class TorrentConfig
{
    public string TorrServeUrl { get; set; } = "http://127.0.0.1:8090";

    /// <summary>Downloads allowed to talk to TorrServe at the same time.</summary>
    public int MaxConcurrentDownloads { get; set; } = 2;

    /// <summary>Downloads allowed to wait for a slot; beyond this they are refused with 503.</summary>
    public int MaxQueuedDownloads { get; set; } = 8;

    /// <summary>How long a queued download waits for a slot before it is refused.</summary>
    public int QueueTimeoutSeconds { get; set; } = 30;

    /// <summary>How long the torrent's file list is trusted before the torrent is re-added. 0 disables.</summary>
    public int FileListCacheMinutes { get; set; } = 10;

    /// <summary>Downloads one client address may start per minute; beyond this, 429. 0 disables.</summary>
    public int DownloadsPerMinutePerClient { get; set; } = 10;
}
