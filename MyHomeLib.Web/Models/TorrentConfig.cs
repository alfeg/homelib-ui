namespace MyHomeLib.Web.Models;

public class TorrentConfig
{
    public string TorrServeUrl { get; set; } = "http://127.0.0.1:8090";

    /// <summary>
    /// How many minutes of inactivity before the library torrent is removed from TorrServe.
    /// Set to 0 to disable sleep mode. Default is 10 minutes.
    /// </summary>
    public int TorrentSleepAfterMinutes { get; set; } = 10;
}
