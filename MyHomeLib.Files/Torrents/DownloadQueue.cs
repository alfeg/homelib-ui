using System.Collections.Concurrent;
using System.Threading.Channels;
using MonoTorrent.Client;

namespace MyHomeLib.Files.Torrents;

public class DownloadQueue
{
    private readonly ClientEngine clientEngine;

    private readonly ConcurrentDictionary<string, Channel<DownloadRequest>> _queue = new();

    public DownloadQueue(ClientEngine clientEngine)
    {
        this.clientEngine = clientEngine;
    }

    public async Task AddToQueue(DownloadRequest request)
    {
        var channel = _queue.GetOrAdd(request.Library, _ => Channel.CreateUnbounded<DownloadRequest>());
    }
}