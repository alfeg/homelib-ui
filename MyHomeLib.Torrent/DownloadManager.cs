using System.Collections.Concurrent;
using System.IO.Compression;
using Fb2.Document;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;

namespace MyHomeListServer.Torrent;

public class DownloadManager : IAsyncDisposable
{
    private readonly ClientEngine? _clientEngine;
    private readonly TorrServeClient? _torrServe;
    private readonly HttpClient? _httpClient;
    private readonly AppConfig _appConfig;
    private readonly ILogger<DownloadManager> _logger;

    /// <summary>Persistent torrent managers keyed by info-hash hex — started once, kept running.</summary>
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);

    /// <summary>MonoTorrent mode constructor.</summary>
    public DownloadManager(ClientEngine clientEngine, IOptions<AppConfig> config, ILogger<DownloadManager> logger)
    {
        _clientEngine = clientEngine;
        _appConfig = config.Value;
        _logger = logger;
    }

    /// <summary>TorrServe mode constructor.</summary>
    public DownloadManager(TorrServeClient torrServe, HttpClient httpClient,
        IOptions<AppConfig> config, ILogger<DownloadManager> logger)
    {
        _torrServe  = torrServe;
        _httpClient = httpClient;
        _appConfig  = config.Value;
        _logger     = logger;
    }

    public async Task<SearchResponse?> SearchFiles(SearchRequest request, CancellationToken ct = default)
    {
        if (_torrServe != null)
            return await SearchViaTorrServe(request, ct);
        var response = await ProcessQueue(request, ct);
        return response as SearchResponse;
    }

    public async Task<DownloadResponse?> DownloadFile(DownloadRequest request, CancellationToken ct = default)
    {
        if (_torrServe != null)
            return await DownloadFileViaTorrServe(request, ct);
        var response = await ProcessQueue(request, ct);
        return response as DownloadResponse;
    }

    /// <summary>Proactively starts the library torrent so DHT/peers begin connecting at app startup.</summary>
    public async Task StartLibraryAsync(MagnetLink link)
    {
        if (_torrServe != null)
        {
            var hash = link.InfoHashes.V1OrV2.ToHex();
            _logger.LogInformation("[{Hash}] Pre-starting library torrent via TorrServe", hash);
            await _torrServe.AddTorrentAsync(link.ToV1String(), link.Name ?? hash, saveToDb: true);
            return;
        }
        await GetOrCreateManagerAsync(link.InfoHashes.V1OrV2.ToHex(), link);
    }

    /// <summary>Returns a snapshot of the current torrent stats for a given info-hash, or null if not running.</summary>
    public TorrentStats? GetStats(string hash)
    {
        if (_torrServe != null)
            return _torrServeStats;
        if (!_managers.TryGetValue(hash, out var manager))
            return null;
        return new TorrentStats(
            manager.Monitor.DownloadRate,
            manager.Monitor.UploadRate,
            manager.Monitor.DataBytesReceived + manager.Monitor.ProtocolBytesReceived,
            manager.Peers.Seeds,
            manager.Peers.Leechs,
            manager.PartialProgress,
            manager.State.ToString(),
            _clientEngine!.Dht.NodeCount,
            _clientEngine!.Dht.State.ToString());
    }

    private TorrentStats? _torrServeStats;

    /// <summary>Called by DownloadQueueService sampler to refresh TorrServe stats.</summary>
    public async Task RefreshTorrServeStatsAsync(string hash, CancellationToken ct = default)
    {
        if (_torrServe == null) return;
        var t = await _torrServe.GetTorrentAsync(hash, ct);
        if (t == null) { _torrServeStats = null; return; }
        _torrServeStats = new TorrentStats(0, 0, 0, 0, 0, 0,
            t.StatString, 0, $"stat={t.Stat}");
    }

    /// <summary>
    /// Returns the persistent <see cref="TorrentManager"/> for this hash, creating and starting it
    /// if it doesn't already exist.  The manager is never stopped between requests.
    /// </summary>
    private async Task<TorrentManager> GetOrCreateManagerAsync(string hash, MagnetLink link)
    {
        if (_managers.TryGetValue(hash, out var existing))
        {
            if (existing.State is TorrentState.Stopped or TorrentState.Error)
            {
                _logger.LogInformation("[{Hash}] Restarting stopped/errored manager", hash);
                await existing.StartAsync();
            }
            return existing;
        }

        await _createLock.WaitAsync();
        try
        {
            // Re-check inside the lock — another caller may have created it while we waited.
            if (_managers.TryGetValue(hash, out existing))
            {
                if (existing.State is TorrentState.Stopped or TorrentState.Error)
                    await existing.StartAsync();
                return existing;
            }

            // Reuse a manager already registered with the engine (e.g. resumed from fast-resume)
            var manager = _clientEngine.Torrents.FirstOrDefault(t => t.InfoHashes.V1OrV2.ToHex() == hash)
                          ?? await _clientEngine.AddStreamingAsync(link, _appConfig.TorrentsFolder(hash));

            _managers[hash] = manager;

            manager.TorrentStateChanged += (_, args) =>
                _logger.LogInformation("[{Hash}] Torrent state: {Old} → {New}", hash, args.OldState, args.NewState);

            if (manager.State is TorrentState.Stopped or TorrentState.Error || !manager.HasMetadata)
            {
                _logger.LogInformation("[{Hash}] Starting persistent streaming manager", hash);
                await manager.StartAsync();
            }

            return manager;
        }
        finally
        {
            _createLock.Release();
        }
    }

    private async Task<TorrentResponse> ProcessQueue(TorrentRequest request,
        CancellationToken cancellationToken = default)
    {
        var hash = request.Link?.InfoHashes.V1OrV2.ToHex() ?? request.Library;

        _logger.LogInformation("[{Hash}] Processing request {Request}", hash, request);

        var infoHash = request.Link?.InfoHashes ?? InfoHashes.FromInfoHash(InfoHash.FromHex(hash));
        var announceUrls = (request.Link?.AnnounceUrls ?? new List<string>())
            .Union(_appConfig.AnnounceUrls).ToList();
        var link = new MagnetLink(infoHash, request.Link?.Name, announceUrls);

        var manager = await GetOrCreateManagerAsync(hash, link);

        TorrentResponse? response = null;

        // Background stats sampler — feeds IProgress<TorrentStats> every second while active
        using var statsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(async () =>
        {
            while (!statsCts.Token.IsCancellationRequested)
            {
                var stats = new TorrentStats(
                    manager.Monitor.DownloadRate,
                    manager.Monitor.UploadRate,
                    manager.Monitor.DataBytesReceived + manager.Monitor.ProtocolBytesReceived,
                    manager.Peers.Seeds,
                    manager.Peers.Leechs,
                    manager.PartialProgress,
                    manager.State.ToString(),
                    _clientEngine.Dht.NodeCount,
                    _clientEngine.Dht.State.ToString());

                request.Progress?.Report(stats);

                try { await Task.Delay(1000, statsCts.Token); } catch { break; }
            }
        }, statsCts.Token);

        try
        {
            if (!manager.HasMetadata)
            {
                _logger.LogInformation("[{Hash}] Waiting for metadata", hash);
                await manager.WaitForMetadataAsync(cancellationToken);
            }

            await manager.SetDownloadOnly("");

            var torrentPath = _appConfig.TorrentPath(manager.InfoHashes.V1OrV2);
            if (!File.Exists(torrentPath) && manager.Torrent != null)
                File.Copy(manager.MetadataPath, torrentPath);

            switch (request)
            {
                case DownloadRequest downloadRequest:
                    response = await DownloadFileInternal(manager, downloadRequest, cancellationToken);
                    break;
                case SearchRequest searchRequest:
                    var pattern = new WildcardPattern(searchRequest.FilePattern);
                    var result = manager.Files.Select(f => f.Path).Where(f => pattern.IsMatch(f)).ToArray();
                    response = new SearchResponse(result);
                    break;
            }
        }
        finally
        {
            await statsCts.CancelAsync();
            // Manager stays running — do not call StopAsync
            RemoveOldArchivesPolicy(manager);
        }

        return response ??
               throw new NotImplementedException($"Request of type: {request.GetType()} is not implemented");
    }

    private void RemoveOldArchivesPolicy(TorrentManager manager)
    {
        var oldFilePolicySpan = TimeSpan.FromHours(1);

        foreach (var file in manager.Files)
        {
            var info = new FileInfo(file.DownloadCompleteFullPath);
            if (info.Name.EndsWith("inpx"))
                continue;

            if (info.Exists && (DateTime.UtcNow - info.LastAccessTimeUtc) > oldFilePolicySpan)
            {
                File.Delete(info.FullName);
                _logger.LogInformation("[{Hash}] File {File} deleted by policy (older than {Age})",
                    manager.InfoHashes.V1OrV2.ToHex(), info.Name, oldFilePolicySpan.Humanize());
                // No SetNeedsHashCheckAsync — manager stays running; MonoTorrent will detect
                // missing pieces and re-download them on the next request for this file.
            }
        }
    }

    private async Task<DownloadResponse> DownloadFileInternal(TorrentManager manager, DownloadRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Library}] {Name} Starting download", request.Library, request.Name);

        var archive = request.Archive;
        var book = request.Book;

        var fileToDownload = manager.Files.First(f => f.Path.EndsWith(archive));
        await manager.SetDownloadOnly(fileToDownload.Path);
        await using var archiveFileStream =
            fileToDownload.BitField.AllTrue
                ? File.Open(fileToDownload.DownloadCompleteFullPath, FileMode.Open)
                : await manager.StreamProvider.CreateStreamAsync(fileToDownload, false, cancellationToken);

        if (fileToDownload.BitField.AllTrue)
            new FileInfo(fileToDownload.DownloadCompleteFullPath).LastAccessTimeUtc = DateTime.UtcNow;

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await archiveFileStream.CopyToAsync(bookStream, cancellationToken);
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty,
                fileToDownload.DownloadCompleteFullPath);
        }

        _logger.LogInformation("[{Library}] {Name} Reading archive entry", request.Library, request.Name);

        using (var zip = new ZipArchive(archiveFileStream))
        {
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
            var title = fb2.Title?.Content.FirstOrDefault(c => c.Name == "book-title")?.ToString()
                        ?? Path.GetFileNameWithoutExtension(book);

            _logger.LogInformation("[{Library}] {Name} Downloaded {Title}.fb2", request.Library, request.Name, title);
            return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2",
                fileToDownload.DownloadCompleteFullPath);
        }

        _logger.LogInformation("[{Library}] {Name} Downloaded {Book}", request.Library, request.Name, book);
        return new DownloadResponse(bookStream.ToArray(), "application/fb2", book,
            fileToDownload.DownloadCompleteFullPath);
    }

    // ── TorrServe paths ─────────────────────────────────────────────────────

    private async Task<SearchResponse> SearchViaTorrServe(SearchRequest request, CancellationToken ct)
    {
        var hash = request.Link?.InfoHashes.V1OrV2.ToHex() ?? request.Library;
        var magnetUri = request.Link?.ToV1String()
                        ?? throw new InvalidOperationException("MagnetLink required for TorrServe search");

        await _torrServe!.AddTorrentAsync(magnetUri, hash, saveToDb: true, ct);
        var files = await _torrServe.WaitForFilesAsync(hash, ct);
        var pattern = new WildcardPattern(request.FilePattern);
        var names = files.Select(f => f.Path).Where(p => pattern.IsMatch(p)).ToArray();
        return new SearchResponse(names);
    }

    private async Task<DownloadResponse> DownloadFileViaTorrServe(DownloadRequest request, CancellationToken ct)
    {
        var hash = request.Link?.InfoHashes.V1OrV2.ToHex() ?? request.Library;
        var archive = request.Archive;
        var book = request.Book;

        _logger.LogInformation("[TorrServe][{Library}] {Name} Starting download", request.Library, request.Name);

        var files = await _torrServe!.WaitForFilesAsync(hash, ct);
        var file = files.FirstOrDefault(f => f.Path.EndsWith(archive))
                   ?? throw new FileNotFoundException($"Archive {archive} not found in torrent {hash}");

        var streamUrl = _torrServe.GetStreamUrl(hash, file.Id);
        await using var archiveStream = new HttpRangeStream(_httpClient!, streamUrl, file.Length);

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await archiveStream.CopyToAsync(bookStream, ct);
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty, null);
        }

        _logger.LogInformation("[TorrServe][{Library}] {Name} Reading archive entry", request.Library, request.Name);
        using (var zip = new ZipArchive(archiveStream))
        {
            var entry = zip.GetEntry(book)
                        ?? throw new FileNotFoundException($"Entry {book} not found in {archive}");
            await entry.Open().CopyToAsync(bookStream, ct);
        }

        bookStream.Seek(0, SeekOrigin.Begin);
        if (book.EndsWith(".fb2"))
        {
            var fb2 = new Fb2Document();
            await fb2.LoadAsync(bookStream);
            bookStream.Seek(0, SeekOrigin.Begin);
            var title = fb2.Title?.Content.FirstOrDefault(c => c.Name == "book-title")?.ToString()
                        ?? Path.GetFileNameWithoutExtension(book);
            _logger.LogInformation("[TorrServe][{Library}] {Name} Downloaded {Title}.fb2",
                request.Library, request.Name, title);
            return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2", null);
        }

        _logger.LogInformation("[TorrServe][{Library}] {Name} Downloaded {Book}",
            request.Library, request.Name, book);
        return new DownloadResponse(bookStream.ToArray(), string.Empty, book, null);
    }

    /// <summary>Gracefully stop all running torrent managers on app shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        // Use a short timeout so unreachable trackers don't stall shutdown.
        var stopTimeout = TimeSpan.FromSeconds(3);
        var tasks = _managers.Values.Select(async manager =>
        {
            try { await manager.StopAsync(stopTimeout); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping manager {Hash}", manager.InfoHashes.V1OrV2.ToHex()); }
        });
        await Task.WhenAll(tasks);
        _managers.Clear();
    }
}