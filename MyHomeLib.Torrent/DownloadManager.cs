using System.IO.Compression;
using Fb2.Document;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;

namespace MyHomeListServer.Torrent;

public class DownloadManager
{
    private readonly ClientEngine _clientEngine;
    private readonly AppConfig _appConfig;
    private readonly ILogger<DownloadManager> _logger;

    public DownloadManager(ClientEngine clientEngine, IOptions<AppConfig> config, ILogger<DownloadManager> logger)
    {
        _clientEngine = clientEngine;
        _appConfig = config.Value;
        _logger = logger;
    }

    public async Task<SearchResponse> SearchFiles(SearchRequest request)
    {
        var response = await ProcessQueue(request);
        return response as SearchResponse;
    }

    public async Task<DownloadResponse> DownloadFile(DownloadRequest request)
    {
        var response = await ProcessQueue(request);
        return response as DownloadResponse;
    }

    private async Task<TorrentResponse> ProcessQueue(TorrentRequest request,
        CancellationToken cancellationToken = default)
    {
        var hash = request.Link?.InfoHashes.V1OrV2.ToHex() ?? request.Library;

        _logger.LogInformation("[{Hash}] Processing request {Request}", hash, request);
        var manager = _clientEngine.Torrents.FirstOrDefault(t => t.InfoHashes.V1OrV2.ToHex() == hash);

        if (manager == null)
        {
            var infoHash = request.Link?.InfoHashes ?? InfoHashes.FromInfoHash(InfoHash.FromHex(hash));
            var announceUrls = request.Link?.AnnounceUrls ?? new List<string>();
            announceUrls = announceUrls.Union(_appConfig.AnnounceUrls).ToList();
            var link = new MagnetLink(infoHash, request.Link?.Name, announceUrls);

            _logger.LogInformation("[{Hash}] Adding streaming manager", hash);
            manager = await _clientEngine.AddStreamingAsync(link, _appConfig.TorrentsFolder(hash));
        }

        TorrentResponse? response = null;

        // Background stats sampler — feeds IProgress<TorrentStats> every second while active
        using var statsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Progress is not null)
        {
            _ = Task.Run(async () =>
            {
                while (!statsCts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(1000, statsCts.Token); } catch { break; }
                    request.Progress.Report(new TorrentStats(
                        manager.Monitor.DownloadRate,
                        manager.Monitor.UploadRate,
                        manager.Monitor.DataBytesReceived + manager.Monitor.ProtocolBytesReceived,
                        manager.Peers.Seeds,
                        manager.Peers.Leechs,
                        manager.PartialProgress));
                }
            }, statsCts.Token);
        }

        void OnTorrentStateChanged(object? sender, TorrentStateChangedEventArgs args)
            => _logger.LogInformation("[{Hash}] Torrent state: {Old} → {New}", hash, args.OldState, args.NewState);

        manager.TorrentStateChanged += OnTorrentStateChanged;

        try
        {
            if (request.RequireStartStop || !manager.HasMetadata)
            {
                _logger.LogInformation("[{Hash}] Starting streaming manager", hash);
                await manager.StartAsync();
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
            manager.TorrentStateChanged -= OnTorrentStateChanged;
            _logger.LogInformation("[{Hash}] Stopping streaming manager", hash);
            try { await manager.StopAsync(); } catch { }
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
}