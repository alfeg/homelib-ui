using System.Collections.Concurrent;
using Fb2.Document;
using Humanizer;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MyHomeLib.Files.Core;

namespace MyHomeLib.Files.Torrents;

public class DownloadManager
{
    private readonly ClientEngine _clientEngine;
    private readonly ILogger<DownloadManager> _logger;

    public DownloadManager(ClientEngine clientEngine, IOptions<AppConfig> config, ILogger<DownloadManager> logger)
    {
        this._clientEngine = clientEngine;
        _logger = logger;
        this._appConfig = config.Value;
    }

    private readonly Gate _gate = new(new SemaphoreSlim(1));

    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueueItem>> _queue = new();
    private readonly AppConfig _appConfig;

    private async Task<Task<DownloadResponse>> AddToQueue(DownloadRequest request)
    {
        using var _ = await _gate.Wait();

        var queue = _queue.GetOrAdd(request.Library, hash =>
        {
            var internalQueue = new ConcurrentQueue<QueueItem>();
            Task.Run(() => ProcessQueue(hash, internalQueue, CancellationToken.None));
            return internalQueue;
        });

        var existing = queue.FirstOrDefault(q => q.Request == request);
        if (existing != null)
        {
            _logger.LogInformation("{Request}: Already in queue", request);
            return existing.TokenSource.Task;
        }

        var queueItem = new QueueItem(request, new TaskCompletionSource<DownloadResponse>());
        queue.Enqueue(queueItem);
        _logger.LogInformation("{Request}: Added to queue", request);
        return queueItem.TokenSource.Task;
    }

    public async Task<DownloadResponse> DownloadFile(DownloadRequest request)
    {
        var task = await AddToQueue(request);
        return await task;
    }

    record QueueItem(DownloadRequest Request, TaskCompletionSource<DownloadResponse> TokenSource);

    private async Task ProcessQueue(string hash, ConcurrentQueue<QueueItem> queue, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Hash}] Creating torrent manager", hash);
        var manager = _clientEngine.Torrents.FirstOrDefault(t => t.InfoHash.ToHex() == hash);

        if (manager == null)
        {
            var link = new MagnetLink(InfoHash.FromHex(hash));
            _logger.LogInformation("[{Hash}] Downloading .torrent file", hash);
            var torrentFile = await _clientEngine.DownloadTorrentFileAsync(link, _appConfig);

            _logger.LogInformation("[{Hash}] Adding streaming manager", hash);
            manager = await _clientEngine.AddStreamingAsync(torrentFile, _appConfig.TorrentsFolder(hash));
            await manager.TrackerManager.AddTrackerAsync(new Uri("http://192.168.3.26:9000/announce"));
        }

        // manager.PeerConnected += (sender, args) =>
        // {
        //    // _logger.LogInformation("[{Hash}] Peer connected: {Peer}", hash, args.Peer);
        // };
        //
        // manager.PeerDisconnected += (sender, args) =>
        // {
        //     //_logger.LogInformation("[{Hash}] Peer disconnected: {Peer}", hash, args.Peer);
        // };

        while (true)
        {
            if (queue.Count > 0)
            {
                _logger.LogInformation("[{Hash}] Start streaming manager", hash);
                await manager.StartAsync();

                while (queue.TryPeek(out var item))
                {
                    var cancel = new CancellationTokenSource();

                    var download = DownloadFileInternal(manager, item.Request, cancellationToken);
                    await Task.WhenAny(download,
                        Task.Run(async () =>
                        {
                            while (!cancel.Token.IsCancellationRequested)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(3), cancel.Token);
                                _logger.LogInformation("[{Hash}] D/U: {Download} / {Upload} (Seeds/Leechs: {Seeds}/{Peers})", 
                                    hash, 
                                    manager.Monitor.DownloadSpeed.Bytes().Per(TimeSpan.FromSeconds(1)).Humanize(),
                                    manager.Monitor.UploadSpeed.Bytes().Per(TimeSpan.FromSeconds(1)).Humanize(),
                                    manager.Peers.Seeds, manager.Peers.Leechs);
                            }   
                        }));
                    cancel.Cancel();
                    item.TokenSource.SetResult(await download);

                    queue.TryDequeue(out _);
                    await Task.Delay(1000, cancellationToken);
                }

                _logger.LogInformation("[{Hash}] Stop streaming manager", hash);
                manager.SaveFastResume();
                await manager.StopAsync();
            }

            await Task.Delay(2000, cancellationToken);
        }
    }

    private async Task<DownloadResponse> DownloadFileInternal(TorrentManager manager, DownloadRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Hash}] {Request} Starting download of file", request.Library, request.Name);

        var archive = request.Archive;
        var book = request.Book;

        var fileToDownload = manager.Files.First(f => f.Path == archive);
        await manager.SetDownloadOnly(fileToDownload.Path);
        await manager.StartAsync();

        await using var stream =
            await manager.StreamProvider.CreateStreamAsync(fileToDownload, false, cancellationToken);

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await stream.CopyToAsync(bookStream, cancellationToken);
            await manager.StopAsync();
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty);
        }

        stream.Seek(0, SeekOrigin.Begin);

        _logger.LogInformation("[{Hash}] {Request} Reading archive header", request.Library, request.Name);
        var buffer = new byte[200_000];
        await stream.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);

        stream.Seek(0, SeekOrigin.Begin);

        using var zip = new ZipFile(stream);
        var entry = zip.GetEntry(book);

        stream.Seek(entry.Offset, SeekOrigin.Begin);

        var resultBuffer = new byte[entry.CompressedSize];
        await stream.ReadAtLeastAsync(resultBuffer, resultBuffer.Length, false, cancellationToken);
        var entryStream = zip.GetInputStream(entry);

        _logger.LogInformation("[{Hash}] {Request} Reading book data", request.Library, request.Name);
        await entryStream.CopyToAsync(bookStream, cancellationToken);

        bookStream.Seek(0, SeekOrigin.Begin);
        if (book.EndsWith(".fb2"))
        {
            var fb2 = new Fb2Document();
            await fb2.LoadAsync(bookStream);
            bookStream.Seek(0, SeekOrigin.Begin);
            var title = fb2.Title?.Content.FirstOrDefault(c => c.Name == "book-title")?.ToString();

            title ??= Path.GetFileNameWithoutExtension(book);

            _logger.LogInformation("[{Hash}] {Request} Downloaded {BookTitle}.fb2", request.Library, request.Name,
                title);
            return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2");
        }

        _logger.LogInformation("[{Hash}] {Request} Downloaded {BookTitle}", request.Library, request.Name, book);
        return new DownloadResponse(bookStream.ToArray(), "application/fb2", book);
    }
}