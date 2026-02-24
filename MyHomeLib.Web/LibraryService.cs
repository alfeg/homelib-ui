using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using MyHomeListServer.Torrent;

namespace MyHomeLib.Web;

public sealed class LibraryService(
    IOptions<LibraryConfig> config,
    IServiceProvider sp,
    AuditService audit,
    ILogger<LibraryService> logger) : BackgroundService, IAsyncDisposable
{
    private readonly LibraryConfig _config = config.Value;

    private readonly TaskCompletionSource<BookSearchIndex> _indexTcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<BookSearchIndex> IndexTask => _indexTcs.Task;
    public InpxLibrary Metadata { get; } = new();
    public string LoadStatus { get; private set; } = "Initialising…";
    public TorrentStats? InpxStats { get; private set; }
    public string MagnetUri => _config.MagnetUri;
    public long TotalBooks => IndexTask.IsCompletedSuccessfully ? IndexTask.Result.TotalBooks : Metadata.BooksAdded;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_config.TorrentEnabled)
            {
                var dm = sp.GetRequiredService<DownloadManager>();

                // Wait until TorrServe is reachable before proceeding
                await WaitForTorrServeAsync(dm, stoppingToken);

                try { await dm.StartLibraryAsync(_config.MagnetUri, stoppingToken); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to pre-register library torrent"); }
            }

            var index = await BuildIndexAsync(stoppingToken);
            _indexTcs.TrySetResult(index);
        }
        catch (OperationCanceledException)
        {
            _indexTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library initialisation failed");
            _indexTcs.TrySetException(ex);
        }
    }

    private async Task WaitForTorrServeAsync(DownloadManager dm, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await dm.RefreshStatsAsync("", ct);
                if (dm.GetStats()?.IsConnected == true)
                {
                    logger.LogInformation("TorrServe is reachable");
                    return;
                }
            }
            catch { /* not ready yet */ }

            LoadStatus = "Waiting for TorrServe…";
            logger.LogDebug("TorrServe not reachable, retrying in 3 s…");
            await Task.Delay(3000, ct);
        }
    }

    private async Task<BookSearchIndex> BuildIndexAsync(CancellationToken ct)
    {
        var inpxPath = await ResolveInpxPathAsync(ct);

        var dbPath = !string.IsNullOrWhiteSpace(_config.LibraryDbPath)
            ? _config.LibraryDbPath
            : Path.ChangeExtension(inpxPath, ".db");

        LoadStatus = "Parsing library…";
        var reader = new InpxReader();
        var books  = reader.ReadLibraryAsync(inpxPath, Metadata);

        return await BookSearchIndex.BuildAsync(
            books,
            dbPath,
            _config.DuckDbMemoryLimitMb,
            status => LoadStatus = status,
            logger);
    }

    private async Task<string> ResolveInpxPathAsync(CancellationToken ct)
    {
        // 1. Explicit path
        if (!string.IsNullOrWhiteSpace(_config.InpxPath))
        {
            LoadStatus = "Loading INPX from configured path…";
            logger.LogInformation("Using configured InpxPath: {Path}", _config.InpxPath);
            return _config.InpxPath;
        }

        // 2. Already on disk
        if (!string.IsNullOrWhiteSpace(_config.DownloadsDirectory)
            && Directory.Exists(_config.DownloadsDirectory))
        {
            var existing = Directory.GetFiles(_config.DownloadsDirectory, "*.inpx").FirstOrDefault();
            if (existing is not null)
            {
                LoadStatus = $"Loading INPX from {Path.GetFileName(existing)}…";
                logger.LogInformation("Found existing INPX at {Path}", existing);
                return existing;
            }
        }

        // 3. Download from torrent
        if (!_config.TorrentEnabled)
            throw new InvalidOperationException(
                "No INPX file found. Set Library:InpxPath, or configure Library:MagnetUri " +
                "and Library:DownloadsDirectory for automatic download.");

        LoadStatus = "Searching torrent for INPX file…";
        logger.LogInformation("No INPX found locally — downloading via TorrServe…");

        var dm   = sp.GetRequiredService<DownloadManager>();
        var hash = MagnetUriHelper.ParseInfoHash(_config.MagnetUri);

        var searchResp = await dm.SearchFiles(
            new SearchRequest(hash, "*.inpx") { MagnetUri = _config.MagnetUri }, ct);
        var inpxEntry = searchResp?.Names.FirstOrDefault()
            ?? throw new InvalidOperationException("No *.inpx file found in the torrent.");

        LoadStatus = $"Downloading {Path.GetFileName(inpxEntry)}…";
        logger.LogInformation("Downloading INPX {File}…", inpxEntry);

        var progress = new Progress<TorrentStats>(s => InpxStats = s);
        var dlResp   = await dm.DownloadFile(
            new DownloadRequest(hash, inpxEntry, null)
            {
                MagnetUri = _config.MagnetUri,
                Progress  = progress
            }, ct);
        InpxStats = null;

        Directory.CreateDirectory(_config.DownloadsDirectory);
        var savePath = Path.Combine(_config.DownloadsDirectory, Path.GetFileName(inpxEntry));
        await File.WriteAllBytesAsync(savePath, dlResp!.Data, ct);
        logger.LogInformation("INPX saved to {Path}", savePath);

        return savePath;
    }

    public async Task<(IReadOnlyList<BookItem> Page, int Total)> SearchAsync(string? query, string? language = null, int max = 200)
    {
        var index = await IndexTask;
        var result = await index.SearchAsync(query, language, max);
        _ = audit.LogSearchAsync(query ?? string.Empty, result.Total);
        return result;
    }

    public async Task<IReadOnlyList<string>> GetLanguagesAsync()
    {
        var index = await IndexTask;
        return await index.GetLanguagesAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (IndexTask.IsCompletedSuccessfully)
            await IndexTask.Result.DisposeAsync();
    }
}
