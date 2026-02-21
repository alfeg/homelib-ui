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

    /// <summary>Human-readable description of the current loading step.</summary>
    public string LoadStatus { get; private set; } = "Initialising…";

    /// <summary>Live torrent stats when the INPX file itself is being downloaded.</summary>
    public TorrentStats? InpxStats { get; private set; }

    public LibraryService(IOptions<LibraryConfig> config, IServiceProvider sp, ILogger<LibraryService> logger)
    {
        LoadTask = LoadAsync(config.Value, sp, Metadata, logger);
        IndexTask = BuildIndexAsync(logger);
    }

    private async Task<IList<BookItem>> LoadAsync(
        LibraryConfig config, IServiceProvider sp, InpxLibrary metadata, ILogger logger)
    {
        var path = await ResolveInpxPathAsync(config, sp, logger);
        LoadStatus = "Parsing library…";
        var reader = new InpxReader();
        var books = new List<BookItem>();
        await foreach (var book in reader.ReadLibraryAsync(path, metadata))
            books.Add(book);
        LoadStatus = $"Loaded {books.Count:N0} books.";
        return books;
    }

    private async Task<string> ResolveInpxPathAsync(
        LibraryConfig config, IServiceProvider sp, ILogger logger)
    {
        // 1. Explicit path
        if (!string.IsNullOrWhiteSpace(config.InpxPath))
        {
            LoadStatus = "Loading INPX from configured path…";
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
                LoadStatus = $"Loading INPX from {Path.GetFileName(existing)}…";
                logger.LogInformation("Found existing INPX at {Path}", existing);
                return existing;
            }
        }

        // 3. Download from torrent
        if (!config.TorrentEnabled)
            throw new InvalidOperationException(
                "No INPX file found. Configure Library:InpxPath, or set Library:MagnetUri " +
                "and Library:DownloadsDirectory so the INPX can be downloaded automatically.");

        LoadStatus = "Searching torrent for INPX file…";
        logger.LogInformation("No INPX found locally — downloading from torrent…");
        var dm = sp.GetRequiredService<DownloadManager>();
        var link = MagnetLink.Parse(config.MagnetUri);
        var hash = link.InfoHashes.V1OrV2.ToHex();

        var searchResp = await dm.SearchFiles(new SearchRequest(hash, "*.inpx") { Link = link });
        var inpxEntry = searchResp?.Names.FirstOrDefault()
            ?? throw new InvalidOperationException("No *.inpx file found in the torrent.");

        LoadStatus = $"Downloading {Path.GetFileName(inpxEntry)} from torrent…";
        logger.LogInformation("Downloading INPX {File} from torrent…", inpxEntry);

        var progress = new Progress<TorrentStats>(s => InpxStats = s);
        var dlResp = await dm.DownloadFile(
            new DownloadRequest(hash, inpxEntry, null) { Link = link, Progress = progress });
        InpxStats = null;

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
