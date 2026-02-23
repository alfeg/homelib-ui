namespace MyHomeLib.Web;

public class LibraryConfig
{
    /// <summary>
    /// Optional explicit path to a .inpx file.
    /// If empty, the app looks for *.inpx in <see cref="DownloadsDirectory"/>;
    /// if none is found there, it downloads the INPX from the torrent automatically.
    /// </summary>
    public string InpxPath { get; set; } = string.Empty;

    /// <summary>
    /// Magnet URI for the library torrent. Required for torrent downloads.
    /// Example: magnet:?xt=urn:btih:...
    /// </summary>
    public string MagnetUri { get; set; } = string.Empty;

    /// <summary>Directory where downloaded books are saved. Must be set explicitly.</summary>
    public string DownloadsDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Path to the persistent DuckDB queue file.
    /// Defaults to <c>queue.db</c> inside <see cref="DownloadsDirectory"/> when left empty.
    /// </summary>
    public string QueueDbPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the DuckDB book index file.
    /// Defaults to a <c>.db</c> file alongside the INPX file when left empty.
    /// </summary>
    public string LibraryDbPath { get; set; } = string.Empty;

    /// <summary>
    /// DuckDB memory cap in MB for each database connection.
    /// Set to 0 to keep DuckDB defaults.
    /// </summary>
    public int DuckDbMemoryLimitMb { get; set; } = 32;

    /// <summary>
    /// How many minutes of inactivity before the library torrent is removed from TorrServe.
    /// Set to 0 to disable sleep mode. Default is 10 minutes.
    /// </summary>
    public int TorrentSleepAfterMinutes { get; set; } = 10;

    public bool TorrentEnabled =>
        !string.IsNullOrWhiteSpace(MagnetUri) && !string.IsNullOrWhiteSpace(DownloadsDirectory);
}
