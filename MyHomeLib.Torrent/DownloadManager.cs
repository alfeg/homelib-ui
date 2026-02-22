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
    private readonly ClientEngine _clientEngine;
    private readonly AppConfig _appConfig;
    private readonly ILogger<DownloadManager> _logger;

    /// <summary>Persistent torrent managers keyed by info-hash hex — started once, kept running.</summary>
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();

    public DownloadManager(ClientEngine clientEngine, IOptions<AppConfig> config, ILogger<DownloadManager> logger)
    {
        _clientEngine = clientEngine;
        _appConfig = config.Value;
        _logger = logger;
    }

    public async Task<SearchResponse?> SearchFiles(SearchRequest request, CancellationToken ct = default)
    {
        var response = await ProcessQueue(request, ct);
        return response as SearchResponse;
    }

    public async Task<DownloadResponse?> DownloadFile(DownloadRequest request, CancellationToken ct = default)
    {
        var response = await ProcessQueue(request, ct);
        return response as DownloadResponse;
    }

    /// <summary>
    /// Returns the persistent <see cref="TorrentManager"/> for this hash, creating and starting it
    /// if it doesn't already exist.  The manager is never stopped between requests.
    /// </summary>
    private async Task<TorrentManager> GetOrCreateManagerAsync(string hash, MagnetLink link)
    {
        if (_managers.TryGetValue(hash, out var existing))
        {
            // Restart only if the engine stopped/errored it
            if (existing.State is TorrentState.Stopped or TorrentState.Error)
            {
                _logger.LogInformation("[{Hash}] Restarting stopped/errored manager", hash);
                await existing.StartAsync();
            }
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
            var tick = 0;
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

                if (++tick % 5 == 0)
                {
                    _logger.LogInformation(
                        "[{Hash}] State={State} | DHT={DhtState} nodes={DhtNodes} | " +
                        "Seeds={Seeds} Peers={Peers} | ⬇ {Down:0.0} KB/s ⬆ {Up:0.0} KB/s | " +
                        "Received={Bytes} KB | Progress={Progress:0.0}%",
                        hash,
                        stats.State,
                        stats.DhtState, stats.DhtNodes,
                        stats.Seeds, stats.Peers,
                        stats.DownloadRateBytesPerSec / 1024,
                        stats.UploadRateBytesPerSec / 1024,
                        stats.BytesReceived / 1024,
                        stats.PartialProgress);

                    foreach (var tier in manager.TrackerManager.Tiers)
                        foreach (var tracker in tier.Trackers)
                            _logger.LogInformation(
                                "[{Hash}] Tracker {Url} | Status={Status} LastAnnounce={Last} Failure={Fail}",
                                hash,
                                tracker.Uri,
                                tracker.Status,
                                tracker.TimeSinceLastAnnounce.TotalSeconds < 1e6
                                    ? $"{tracker.TimeSinceLastAnnounce.TotalSeconds:0}s ago"
                                    : "never",
                                string.IsNullOrEmpty(tracker.FailureMessage) ? "none" : tracker.FailureMessage);
                }

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
            await RemoveOldArchivesPolicy(manager);
        }

        return response ??
               throw new NotImplementedException($"Request of type: {request.GetType()} is not implemented");
    }

    private async Task RemoveOldArchivesPolicy(TorrentManager manager)
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
                await manager.SetNeedsHashCheckAsync();
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

    /// <summary>Gracefully stop all running torrent managers on app shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var manager in _managers.Values)
        {
            try { await manager.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping manager {Hash}", manager.InfoHashes.V1OrV2.ToHex()); }
        }
        _managers.Clear();
    }
}