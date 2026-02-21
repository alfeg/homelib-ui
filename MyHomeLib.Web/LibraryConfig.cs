namespace MyHomeLib.Web;

public class LibraryConfig
{
    /// <summary>Path to the .inpx file.</summary>
    public string InpxPath { get; set; } = string.Empty;

    /// <summary>
    /// Magnet URI for the library torrent. Required for torrent downloads.
    /// Example: magnet:?xt=urn:btih:...
    /// </summary>
    public string MagnetUri { get; set; } = string.Empty;

    /// <summary>Directory where downloaded books are saved. Must be set explicitly.</summary>
    public string DownloadsDirectory { get; set; } = string.Empty;

    /// <summary>Path to the persistent DuckDB file used for the download queue.</summary>
    public string QueueDbPath { get; set; } = "queue.db";

    public bool TorrentEnabled =>
        !string.IsNullOrWhiteSpace(MagnetUri) && !string.IsNullOrWhiteSpace(DownloadsDirectory);
}
