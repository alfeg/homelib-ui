using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MyHomeLib.Library;
using MyHomeListServer.Torrent;

namespace MyHomeLib.Web;

public class LibraryService : IAsyncDisposable
{
    public Task<IList<BookItem>> LoadTask { get; }
    public Task<BookSearchIndex> IndexTask { get; }
    public InpxLibrary Metadata { get; } = new();

    public LibraryService(IOptions<LibraryConfig> config, IServiceProvider sp, ILogger<LibraryService> logger)
    {
        LoadTask = LoadAsync(config.Value, sp, Metadata, logger);
        IndexTask = BuildIndexAsync(logger);
    }

    private static async Task<IList<BookItem>> LoadAsync(
        LibraryConfig config, IServiceProvider sp, InpxLibrary metadata, ILogger logger)
    {
        var path = await ResolveInpxPathAsync(config, sp, logger);
        var reader = new InpxReader();
        var books = new List<BookItem>();
        await foreach (var book in reader.ReadLibraryAsync(path, metadata))
            books.Add(book);
        return books;
    }

    /// <summary>
    /// Resolves the INPX path using this priority:
    ///   1. Library:InpxPath (explicit override)
    ///   2. Any *.inpx file already present in Library:DownloadsDirectory
    ///   3. Download *.inpx from the torrent and save to Library:DownloadsDirectory
    /// </summary>
    private static async Task<string> ResolveInpxPathAsync(
        LibraryConfig config, IServiceProvider sp, ILogger logger)
    {
        // 1. Explicit path
        if (!string.IsNullOrWhiteSpace(config.InpxPath))
        {
            logger.LogInformation("Using configured InpxPath: {Path}", config.InpxPath);
            return config.InpxPath;
        }

        // 2. Already on disk in DownloadsDirectory
        if (!string.IsNullOrWhiteSpace(config.DownloadsDirectory)
            && Directory.Exists(config.DownloadsDirectory))
        {
            var existing = Directory.GetFiles(config.DownloadsDirectory, "*.inpx").FirstOrDefault();
            if (existing is not null)
            {
                logger.LogInformation("Found existing INPX at {Path}", existing);
                return existing;
            }
        }

        // 3. Download from torrent
        if (!config.TorrentEnabled)
            throw new InvalidOperationException(
                "No INPX file found. Configure Library:InpxPath, or set Library:MagnetUri " +
                "and Library:DownloadsDirectory so the INPX can be downloaded automatically.");

        logger.LogInformation("No INPX found locally — downloading from torrent…");
        var dm = sp.GetRequiredService<DownloadManager>();
        var link = MagnetLink.Parse(config.MagnetUri);
        var hash = link.InfoHashes.V1OrV2.ToHex();

        // Find *.inpx file inside the torrent
        var searchResp = await dm.SearchFiles(new SearchRequest(hash, "*.inpx") { Link = link });
        var inpxEntry = searchResp?.Names.FirstOrDefault()
            ?? throw new InvalidOperationException("No *.inpx file found in the torrent.");

        logger.LogInformation("Downloading INPX {File} from torrent…", inpxEntry);
        var dlResp = await dm.DownloadFile(
            new DownloadRequest(hash, inpxEntry, null) { Link = link });

        Directory.CreateDirectory(config.DownloadsDirectory);
        var savePath = Path.Combine(config.DownloadsDirectory, Path.GetFileName(inpxEntry));
        await File.WriteAllBytesAsync(savePath, dlResp!.Data);
        logger.LogInformation("INPX saved to {Path}", savePath);

        return savePath;
    }

    private async Task<BookSearchIndex> BuildIndexAsync(ILogger logger)
    {
        var books = await LoadTask;
        return await BookSearchIndex.BuildAsync(books, logger);
    }

    public async Task<(IReadOnlyList<BookItem> Page, int Total)> SearchAsync(string? query, int max = 200)
    {
        var index = await IndexTask;
        return await index.SearchAsync(query, max);
    }

    public async ValueTask DisposeAsync()
    {
        if (IndexTask.IsCompletedSuccessfully)
            await IndexTask.Result.DisposeAsync();
    }
}
