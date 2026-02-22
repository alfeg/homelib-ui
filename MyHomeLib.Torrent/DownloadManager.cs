using System.IO.Compression;
using Fb2.Document;
using Microsoft.Extensions.Logging;

namespace MyHomeListServer.Torrent;

public class DownloadManager(
    TorrServeClient torrServe,
    HttpClient httpClient,
    ILogger<DownloadManager> logger)
{
    private readonly TorrServeClient _torrServe = torrServe;
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<DownloadManager> _logger = logger;

    private volatile TorrentStats? _stats;

    /// <summary>Registers the library torrent with TorrServe so it is ready for streaming.</summary>
    public async Task StartLibraryAsync(string magnetUri, CancellationToken ct = default)
    {
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
        _logger.LogInformation("[TorrServe] Pre-registering library {Hash}", hash);
        await _torrServe.AddTorrentAsync(magnetUri, hash, saveToDb: true, ct);
    }

    public async Task<SearchResponse?> SearchFiles(SearchRequest request, CancellationToken ct = default)
        => await SearchViaTorrServe(request, ct);

    public async Task<DownloadResponse?> DownloadFile(DownloadRequest request, CancellationToken ct = default)
        => await DownloadFileViaTorrServe(request, ct);

    /// <summary>Returns the last refreshed stats snapshot, or null if never refreshed.</summary>
    public TorrentStats? GetStats() => _stats;

    /// <summary>Refreshes the TorrServe connection status and library torrent state.</summary>
    public async Task RefreshStatsAsync(string hash, CancellationToken ct = default)
    {
        var connected = await _torrServe.CheckConnectionAsync(ct);
        if (!connected)
        {
            _stats = new TorrentStats(false, "TorrServe unreachable");
            return;
        }
        var t = await _torrServe.GetTorrentAsync(hash, ct);
        var ts = t?.TorrentStats;
        _stats = new TorrentStats(
            IsConnected:       true,
            State:             t?.StatString ?? "Connected",
            DownloadSpeed:     ts?.DownloadSpeed ?? 0,
            UploadSpeed:       ts?.UploadSpeed ?? 0,
            TotalPeers:        ts?.TotalPeers ?? 0,
            ActivePeers:       ts?.ActivePeers ?? 0,
            ConnectedSeeders:  ts?.ConnectedSeeders ?? 0,
            LoadedSize:        ts?.LoadedSize ?? 0,
            TorrentSize:       ts?.TorrentSize ?? 0
        );
    }

    // ── TorrServe paths ─────────────────────────────────────────────────────

    private async Task<SearchResponse> SearchViaTorrServe(SearchRequest request, CancellationToken ct)
    {
        var hash = request.Library;
        var magnetUri = request.MagnetUri
            ?? throw new InvalidOperationException("MagnetUri required for TorrServe search");

        await _torrServe.AddTorrentAsync(magnetUri, hash, saveToDb: true, ct);
        var files = await _torrServe.WaitForFilesAsync(hash, ct);
        var pattern = new WildcardPattern(request.FilePattern);
        var names = files.Select(f => f.Path).Where(p => pattern.IsMatch(p)).ToArray();
        return new SearchResponse(names);
    }

    private async Task<DownloadResponse> DownloadFileViaTorrServe(DownloadRequest request, CancellationToken ct)
    {
        var hash    = request.Library;
        var archive = request.Archive;
        var book    = request.Book;

        _logger.LogInformation("[TorrServe] [{Library}] {Name} Starting download", request.Library, request.Name);

        var files = await _torrServe.WaitForFilesAsync(hash, ct);
        var file = files.FirstOrDefault(f => f.Path.EndsWith(archive))
                   ?? throw new FileNotFoundException($"Archive {archive} not found in torrent {hash}");

        var streamUrl = _torrServe.GetStreamUrl(hash, file.Id);
        await using var archiveStream = new HttpRangeStream(_httpClient, streamUrl, file.Length);

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await archiveStream.CopyToAsync(bookStream, ct);
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty, null);
        }

        _logger.LogInformation("[TorrServe] [{Library}] {Name} Reading archive entry", request.Library, request.Name);
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
            _logger.LogInformation("[TorrServe] [{Library}] Downloaded {Title}.fb2", request.Library, title);
            return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2", null);
        }

        _logger.LogInformation("[TorrServe] [{Library}] Downloaded {Book}", request.Library, book);
        return new DownloadResponse(bookStream.ToArray(), string.Empty, book, null);
    }
}