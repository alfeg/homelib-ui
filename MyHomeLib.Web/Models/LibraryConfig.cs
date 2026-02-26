namespace MyHomeLib.Web.Models;

public class LibraryConfig
{
    /// <summary>Directory where downloaded books are saved. Must be set explicitly.</summary>
    public string DownloadsDirectory { get; set; } = string.Empty;

    /// <summary>
    /// How many minutes of inactivity before the library torrent is removed from TorrServe.
    /// Set to 0 to disable sleep mode. Default is 10 minutes.
    /// </summary>
    public int TorrentSleepAfterMinutes { get; set; } = 10;
}
