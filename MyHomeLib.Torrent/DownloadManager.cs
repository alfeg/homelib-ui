using System.Collections.Concurrent;
using System.IO.Compression;
using Fb2.Document;
using Humanizer;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using Spectre.Console;

namespace MyHomeListServer.Torrent;

public class DownloadManager
{
    private readonly ClientEngine _clientEngine;

    public DownloadManager(ClientEngine clientEngine, IOptions<AppConfig> config)
    {
        _clientEngine = clientEngine;
        _appConfig = config.Value;
    }

    private readonly AppConfig _appConfig;

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

        AnsiConsole.WriteLine($"[{hash}] Processing request {request}");
        var manager = _clientEngine.Torrents.FirstOrDefault(t => t.InfoHashes.V1OrV2.ToHex() == hash);

        if (manager == null)
        {
            var infoHash = request.Link?.InfoHashes ?? InfoHashes.FromInfoHash(InfoHash.FromHex(hash));
            var announceUrls = request.Link?.AnnounceUrls ?? new List<string>();
            announceUrls = announceUrls.Union(_appConfig.AnnounceUrls).ToList();
            var link = new MagnetLink(infoHash, request.Link?.Name, announceUrls);

            AnsiConsole.WriteLine($"[{hash}] Adding streaming manager");
            manager = await _clientEngine.AddStreamingAsync(link, _appConfig.TorrentsFolder(hash));
        }

        var cancel = new CancellationTokenSource();

        TorrentResponse? response = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Pong)
            .StartAsync("Working", async (ctx) =>
            {
                Task.Run(async () =>
                {
                    while (!cancel.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));

                        ctx.Status(string.Format("[[{0}]] D: {1} ({2}) U: {3} ({4}) (Seeds/Leechs: {5}/{6})",
                            hash,
                            (manager.Monitor.DataBytesReceived + manager.Monitor.ProtocolBytesReceived).Bytes()
                            .Humanize(),
                            manager.Monitor.DownloadRate.Bytes().Per(TimeSpan.FromSeconds(1)).Humanize(),
                            (manager.Monitor.DataBytesSent + manager.Monitor.ProtocolBytesSent).Bytes().Humanize(),
                            manager.Monitor.UploadRate.Bytes().Per(TimeSpan.FromSeconds(1)).Humanize(),
                            manager.Peers.Seeds, manager.Peers.Leechs
                        ));
                    }
                });

                void ManagerOnTorrentStateChanged(object? sender, TorrentStateChangedEventArgs args)
                {
                    AnsiConsole.WriteLine("[{0}] Torrent state changed from {1} to {2}", hash, args.OldState,
                        args.NewState);
                }

                manager.TorrentStateChanged += ManagerOnTorrentStateChanged;

                if (request.RequireStartStop || !manager.HasMetadata)
                {
                    AnsiConsole.WriteLine("[{0}] Start streaming manager", hash);

                    await manager.StartAsync();
                    await manager.WaitForMetadataAsync(cancellationToken);
                }

                await manager.SetDownloadOnly("");

                var torrentPath = _appConfig.TorrentPath(manager.InfoHashes.V1OrV2);
                if (!File.Exists(torrentPath) && manager.Torrent != null)
                {
                    File.Copy(manager.MetadataPath, torrentPath);
                }

                try
                {
                    switch (request)
                    {
                        case DownloadRequest downloadRequest:
                            response = await DownloadFileInternal(manager, downloadRequest, cancellationToken);
                            break;
                        case SearchRequest searchRequest:
                            var pattern = new WildcardPattern(searchRequest.FilePattern);
                            var result = manager.Files
                                .Select(f => f.Path)
                                .Where(f => pattern.IsMatch(f))
                                .ToArray();
                            response = new SearchResponse(result);
                            break;
                    }
                }
                finally
                {
                    AnsiConsole.WriteLine("[{0}] Stop streaming manager", hash);
                    manager.TorrentStateChanged -= ManagerOnTorrentStateChanged;
                    try
                    {
                        await manager.StopAsync();
                    } catch{}

                    cancel.Cancel();

                    await RemoveOldArchivesPolicy(manager);
                }
            });

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
            {
                continue;
            }
            if (info.Exists && (DateTime.UtcNow - info.LastAccessTimeUtc) > oldFilePolicySpan)
            {
                File.Delete(info.FullName);
                AnsiConsole.WriteLine("[{0}] File {1} deleted by policy. Older then {2}",
                    manager.InfoHashes.V1OrV2.ToHex(), info.Name, oldFilePolicySpan.Humanize());
                await manager.SetNeedsHashCheckAsync();
            }
        }
    }

    private async Task<DownloadResponse> DownloadFileInternal(TorrentManager manager, DownloadRequest request,
        CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine("[{0}] {1} Starting download of file", request.Library, request.Name);

        var archive = request.Archive;
        var book = request.Book;

        var fileToDownload = manager.Files.First(f => f.Path == archive);
        await manager.SetDownloadOnly(fileToDownload.Path);
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
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty,
                fileToDownload.DownloadCompleteFullPath);
        }

        AnsiConsole.WriteLine("[{0}] {1} Looking for archive central directory", request.Library,
            request.Name);

        using (var zip = new ZipArchive(archiveFileStream))
        {
            AnsiConsole.WriteLine("[{0}] {1} Reading book data", request.Library, request.Name);
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

            AnsiConsole.WriteLine("[{0}] {1} Downloaded {2}.fb2", request.Library, request.Name,
                title);
            return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2",
                fileToDownload.DownloadCompleteFullPath);
        }

        AnsiConsole.WriteLine("[{0}] {1} Downloaded {2}", request.Library, request.Name, book);
        return new DownloadResponse(bookStream.ToArray(), "application/fb2", book,
            fileToDownload.DownloadCompleteFullPath);
    }
}