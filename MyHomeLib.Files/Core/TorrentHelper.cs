using MonoTorrent;
using MonoTorrent.Client;

namespace MyHomeLib.Files.Core;

public static class TorrentHelper
{
    public static async Task SetDownloadOnly(this TorrentManager st, string path)
    {
        foreach (var torrentFileInfo in st.Files)
        {
            if (torrentFileInfo.Path == path)
            {
                if (torrentFileInfo.Priority != Priority.Normal)
                {
                    await st.SetFilePriorityAsync(torrentFileInfo, Priority.Normal);
                }
                continue;
            };

            await st.SetFilePriorityAsync(torrentFileInfo, Priority.DoNotDownload);
        }
    }

    public static async Task<Torrent> DownloadTorrentFileAsync(this ClientEngine eng, MagnetLink link,
        AppConfig config)
    {
        var torrentFile = config.TorrentPath(link.InfoHashes.V1);
        if (File.Exists(torrentFile))
        {
            return await Torrent.LoadAsync(torrentFile);
        }

        var metadata = await eng.DownloadMetadataAsync(link, CancellationToken.None);
        var torrent = await Torrent.LoadAsync(metadata.ToArray());
        await File.WriteAllBytesAsync(torrentFile, metadata.ToArray());
        return torrent;
    }
}