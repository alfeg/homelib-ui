using MonoTorrent;
using MonoTorrent.Client;

namespace MyHomeLib.Files.Core;

public static class TorrentHelper
{
    public static async Task SetDownloadOnly(this TorrentManager st, string path)
    {
        foreach (var torrentFileInfo in st.Files)
        {
            if (torrentFileInfo.Path == path) continue;

            await st.SetFilePriorityAsync(torrentFileInfo, Priority.DoNotDownload);
        }
    }

    public static async Task<Torrent> DownloadTorrentFileAsync(this ClientEngine eng, MagnetLink link,
        AppConfig config)
    {
        var torrentFile = config.TorrentPath(link.InfoHash);
        if (File.Exists(torrentFile))
        {
            return await Torrent.LoadAsync(torrentFile);
        }

        var metadata = await eng.DownloadMetadataAsync(link, CancellationToken.None);
        var torrent = await Torrent.LoadAsync(metadata);
        await File.WriteAllBytesAsync(torrentFile, metadata);
        return torrent;
    }
}