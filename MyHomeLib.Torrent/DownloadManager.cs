using System.Collections.Concurrent;
using System.IO.Compression;
using Fb2.Document;
using Humanizer;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;

namespace MyHomeListServer.Torrent;

public class DownloadManager
{
    private readonly ClientEngine _clientEngine;
    private readonly ILogger<DownloadManager> _logger;

    public DownloadManager(ClientEngine clientEngine, IOptions<AppConfig> config, ILogger<DownloadManager> logger)
    {
        _clientEngine = clientEngine;
        _logger = logger;
        _appConfig = config.Value;
    }

    private readonly Gate _gate = new(new SemaphoreSlim(1));

    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueueItem>> _queue = new();
    private readonly AppConfig _appConfig;

    private async Task<Task<TorrentResponse>> AddToQueue(TorrentRequest request)
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

        var queueItem = new QueueItem(request, new TaskCompletionSource<TorrentResponse>());
        queue.Enqueue(queueItem);
        _logger.LogInformation("{Request}: Added to queue", request);
        return queueItem.TokenSource.Task;
    }

    public async Task<SearchResponse> SearchFiles(SearchRequest request)
    {
        var task = await AddToQueue(request);
        var response = await task;
        return response as SearchResponse;
    }

    public async Task<DownloadResponse> DownloadFile(DownloadRequest request)
    {
        var task = await AddToQueue(request);
        var response = await task;
        return response as DownloadResponse;
    }

    record QueueItem(TorrentRequest Request, TaskCompletionSource<TorrentResponse> TokenSource);

    private async Task ProcessQueue(string hash, ConcurrentQueue<QueueItem> queue, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Hash}] Creating torrent manager", hash);
        var manager = _clientEngine.Torrents.FirstOrDefault(t => t.InfoHashes.V1.ToHex() == hash);

        if (manager == null)
        {
            var link = new MagnetLink(InfoHash.FromHex(hash), announceUrls: _appConfig.AnnounceUrls);
            _logger.LogInformation("[{Hash}] Adding streaming manager", hash);
            manager = await _clientEngine.AddStreamingAsync(link, _appConfig.TorrentsFolder(hash));
        }

        var cancel = new CancellationTokenSource();
        var monitorLock = new ManualResetEventSlim();

#pragma warning disable CS4014
        Task.Run(async () =>
#pragma warning restore CS4014
        {
            while (!cancel.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancel.Token);
                monitorLock.Wait();
                _logger.LogInformation(
                    "[{Hash}] D: {DownloadTotal} ({DownloadRate}) U: {UploadTotal} ({UploadRate}) (Seeds/Leechs: {Seeds}/{Peers})",
                    hash,
                    (manager.Monitor.DataBytesReceived + manager.Monitor.ProtocolBytesReceived).Bytes().Humanize(),
                    manager.Monitor.DownloadRate.Bytes().Per(TimeSpan.FromSeconds(1)).Humanize(),
                    (manager.Monitor.DataBytesSent + manager.Monitor.ProtocolBytesSent).Bytes().Humanize(),
                    manager.Monitor.UploadRate.Bytes().Per(TimeSpan.FromSeconds(1)).Humanize(),
                    manager.Peers.Seeds, manager.Peers.Leechs);
            }
        }, cancel.Token);

        manager.TorrentStateChanged += (sender, args) =>
        {
            _logger.LogInformation("[{Hash}] Torrent state changed from {OldState} to {NewState}", hash, args.OldState,
                args.NewState);
            if (args.NewState != TorrentState.Stopped)
            {
                monitorLock.Set();
            }
            else
            {
                monitorLock.Reset();
            }
        };

        while (true)
        {
            if (queue.Count > 0)
            {
                _logger.LogInformation("[{Hash}] Start streaming manager", hash);
                await manager.SetDownloadOnly("");
                await manager.StartAsync();
                await manager.WaitForMetadataAsync(cancellationToken);

                while (queue.TryPeek(out var item))
                {
                    try
                    {
                        switch (item.Request)
                        {
                            case DownloadRequest downloadRequest:
                            {
                                var download = await DownloadFileInternal(manager, downloadRequest, cancellationToken);
                                item.TokenSource.SetResult(download);
                                await Task.Delay(1000, cancellationToken);
                                break;
                            }
                            case SearchRequest searchRequest:
                                var pattern = new WildcardPattern(searchRequest.FilePattern);
                                var result = manager.Files
                                    .Select(f => f.Path)
                                    .Where(f => pattern.IsMatch(f))
                                    .ToArray();
                                item.TokenSource.SetResult(new SearchResponse(result));
                                break;
                        }
                    }
                    finally
                    {
                        queue.TryDequeue(out _);
                    }
                }

                _logger.LogInformation("[{Hash}] Stop streaming manager", hash);

                await manager.StopAsync();
            }

            await Task.Delay(2000, cancellationToken);
            await RemoveOldArchivesPolicy(manager);
        }
    }

    private async Task RemoveOldArchivesPolicy(TorrentManager manager)
    {
        var oldFilePolicySpan = TimeSpan.FromHours(1);

        foreach (var file in manager.Files)
        {
            var info = new FileInfo(file.DownloadCompleteFullPath);
            if (info.Exists && (DateTime.UtcNow - info.LastAccessTimeUtc) > oldFilePolicySpan)
            {
                File.Delete(info.FullName);
                _logger.LogWarning("[{Hash}] File {FilName} deleted by policy. Older then {PolicyPeriod}",
                    manager.InfoHashes.V1.ToHex(), info.Name, oldFilePolicySpan.Humanize());
                await manager.SetNeedsHashCheckAsync();
            }
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
        if (manager.State == TorrentState.Stopped)
        {
            await manager.StartAsync();
            await manager.WaitForMetadataAsync(cancellationToken);
        }

        await using var archiveFileStream =
            fileToDownload.BitField.AllTrue
                ? File.Open(fileToDownload.DownloadCompleteFullPath, FileMode.Open)
                : await manager.StreamProvider.CreateStreamAsync(fileToDownload, false, cancellationToken);

        if (fileToDownload.BitField.AllTrue)
        {
            new FileInfo(fileToDownload.DownloadCompleteFullPath).LastAccessTimeUtc = DateTime.UtcNow;
        }

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await archiveFileStream.CopyToAsync(bookStream, cancellationToken);
            await manager.StopAsync();
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty, fileToDownload.DownloadCompleteFullPath);
        }

        _logger.LogInformation("[{Hash}] {Request} Looking for archive central directory", request.Library,
            request.Name);

        using (var zip = new ZipArchive(archiveFileStream))
        {
            _logger.LogInformation("[{Hash}] {Request} Reading book data", request.Library, request.Name);
            var entry = zip.GetEntry(book);
            var entryStream = entry.Open();
            await entryStream.CopyToAsync(bookStream, cancellationToken);
        }

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
            return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2",
                fileToDownload.DownloadCompleteFullPath);
        }

        _logger.LogInformation("[{Hash}] {Request} Downloaded {BookTitle}", request.Library, request.Name, book);
        return new DownloadResponse(bookStream.ToArray(), "application/fb2", book,
            fileToDownload.DownloadCompleteFullPath);
    }
}