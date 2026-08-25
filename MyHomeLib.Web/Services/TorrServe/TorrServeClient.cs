using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Fb2.Document;
using MyHomeLib.Web.Services.Models;

namespace MyHomeLib.Web.Services.TorrServe;

/// <summary>
/// Thin client for the TorrServe HTTP API (https://github.com/YouROK/TorrServer).
/// </summary>
public class TorrServeClient(HttpClient http, ILogger<TorrServeClient> logger)
{
    private readonly string _baseUrl = http.BaseAddress!.ToString().TrimEnd('/');

    /// <summary>Range-request granularity when reading a book out of a torrent archive.</summary>
    private const int ArchiveReadBufferSize = 512 * 1024;

    /// <summary>Attempts allowed when the archive is not yet readable. See DownloadFile.</summary>
    private const int ArchiveReadAttempts = 3;

    private static readonly TimeSpan ArchiveRetryDelay = TimeSpan.FromSeconds(3);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Searches for files in a torrent matching a wildcard pattern.</summary>
    public async Task<SearchResponse> Search(SearchRequest request, CancellationToken ct = default)
    {
        var hash = request.Library;
        var magnetUri = request.MagnetUri
            ?? throw new InvalidOperationException("MagnetUri required for TorrServe search");

        await AddTorrentAsync(magnetUri, hash, saveToDb: true, ct: ct);
        var files = await WaitForFilesAsync(hash, ct);
        var regexPattern = "^" + Regex.Escape(request.FilePattern)
            .Replace("\\\\\\?","??").Replace("\\?", ".").Replace("??","\\?")
            .Replace("\\\\\\*","**").Replace("\\*", ".*").Replace("**","\\*") + "$";
        var names = files.Select(f => f.Path).Where(p => Regex.IsMatch(p, regexPattern)).ToArray();
        return new SearchResponse(names);
    }

    /// <summary>Downloads a book entry from a ZIP archive inside the torrent.</summary>
    /// <remarks>
    /// Retried because a cold archive fails in a specific, recoverable way: TorrServe has not
    /// yet fetched the pieces covering the ZIP central directory, serves short data for the
    /// range, and ZipArchive rejects it with "End of Central Directory record could not be
    /// found". Asking again a moment later succeeds, because the first attempt is what makes
    /// TorrServe go and get those pieces. Observed on 3 of 65 books against a cold torrent.
    /// </remarks>
    public async Task<DownloadResponse> DownloadFile(DownloadRequest request, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(request, ct);
            }
            catch (InvalidDataException ex) when (attempt < ArchiveReadAttempts && !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "[TorrServe] [{Library}] {Name} archive not readable yet on attempt {Attempt}: {Message}",
                    request.Library, request.Name, attempt, ex.Message);

                await Task.Delay(ArchiveRetryDelay, ct);
            }
        }
    }

    private async Task<DownloadResponse> DownloadOnceAsync(DownloadRequest request, CancellationToken ct)
    {
        var hash    = request.Library;
        var archive = request.Archive;
        var book    = request.Book;

        var magnetUri = request.MagnetUri
            ?? throw new InvalidOperationException("MagnetUri required for TorrServe download");

        logger.LogInformation("[TorrServe] [{Library}] {Name} Starting download", request.Library, request.Name);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await AddTorrentAsync(magnetUri, hash, saveToDb: true, ct: ct);
        var files = await WaitForFilesAsync(hash, ct);
        var file = files.FirstOrDefault(f => f.Path.EndsWith(archive))
                   ?? throw new FileNotFoundException($"Archive {archive} not found in torrent {hash}");

        var streamUrl = GetStreamUrl(hash, file.Id);
        await using var rangeStream = new HttpRangeStream(http, streamUrl, file.Length);

        // ZipArchive reads the central directory in many small chunks, and every unbuffered
        // Read here becomes its own HTTP Range request to TorrServe — thousands of round
        // trips before the first byte of the book. Buffering collapses that into a handful.
        await using var archiveStream = new BufferedStream(rangeStream, ArchiveReadBufferSize);

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await archiveStream.CopyToAsync(bookStream, ct);

            // A cold torrent can serve a short range without failing the HTTP request, and a
            // truncated file is not detectable later: the bytes look like a valid archive
            // until something deep inside fails to inflate. TorrServe told us the length, so
            // check it here where a retry is still cheap and the error is still honest.
            if (bookStream.Length != file.Length)
            {
                throw new InvalidDataException(
                    $"{request.Name}: expected {file.Length} bytes from TorrServe but received {bookStream.Length}.");
            }

            logger.LogInformation("[TorrServe] [{Library}] {Name} done in {Elapsed}ms", request.Library, request.Name, sw.ElapsedMilliseconds);
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty);
        }

        using (var zip = new ZipArchive(archiveStream))
        {
            var entry = zip.GetEntry(book)
                        ?? throw new FileNotFoundException($"Entry {book} not found in {archive}");

            await using var entryStream = entry.Open();
            await entryStream.CopyToAsync(bookStream, ct);
        }

        bookStream.Seek(0, SeekOrigin.Begin);
        if (book.EndsWith(".fb2"))
        {
            try
            {
                var fb2 = new Fb2Document();
                await fb2.LoadAsync(bookStream, cancellationToken: ct);
                bookStream.Seek(0, SeekOrigin.Begin);
                var title = fb2.Title?.Content.FirstOrDefault(c => c.Name == "book-title")?.ToString()
                            ?? Path.GetFileNameWithoutExtension(book);
                logger.LogInformation("[TorrServe] [{Library}] Downloaded {Title}.fb2 in {Elapsed}ms", request.Library, title, sw.ElapsedMilliseconds);
                return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[TorrServe] [{Library}] FB2 parsing failed for {Book}, serving raw file", request.Library, book);
                bookStream.Seek(0, SeekOrigin.Begin);
            }
        }

        logger.LogInformation("[TorrServe] [{Library}] Downloaded {Book} in {Elapsed}ms", request.Library, book, sw.ElapsedMilliseconds);
        return new DownloadResponse(bookStream.ToArray(), string.Empty, book);
    }

    /// <summary>Removes a torrent from TorrServe (does not delete cached data).</summary>
    public async Task RemoveTorrentAsync(string hash, CancellationToken ct = default)
    {
        logger.LogDebug("[TorrServe] RemoveTorrent hash={Hash}", hash);
        await PostAsync<TorrentActionRequest, TorrServeTorrent>("/torrents", new TorrentActionRequest("rem", hash),
            TorrServeJsonContext.Default.TorrentActionRequest,
            TorrServeJsonContext.Default.TorrServeTorrent, ct);
    }

    /// <summary>Returns true if TorrServe is reachable (GET /echo). Also captures version string.</summary>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync(_baseUrl + "/echo", ct);
            if (!resp.IsSuccessStatusCode) return false;
            LastVersion = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Last server version string returned by /echo.</summary>
    public string? LastVersion { get; private set; }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Adds a torrent by magnet URI.</summary>
    private async Task AddTorrentAsync(string magnetUri, string title, bool saveToDb = true,
        CancellationToken ct = default)
    {
        logger.LogDebug("[TorrServe] AddTorrent title={Title} saveToDb={Save}", title, saveToDb);
        var body = new TorrentAddRequest("add", magnetUri, title, saveToDb);
        var resp = await PostAsync<TorrentAddRequest, TorrServeTorrent>("/torrents", body,
            TorrServeJsonContext.Default.TorrentAddRequest,
            TorrServeJsonContext.Default.TorrServeTorrent, ct);
        logger.LogDebug("[TorrServe] AddTorrent → hash={Hash} stat={Stat} ({StatStr})",
            resp.Hash, resp.Stat, resp.StatString);
    }

    /// <summary>Polls until TorrServe has fetched torrent metadata and returns the file list.</summary>
    private async Task<TorrServeFile[]> WaitForFilesAsync(string hash, CancellationToken ct = default)
    {
        logger.LogDebug("[TorrServe] WaitForFiles hash={Hash}", hash);
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var t = await GetTorrentAsync(hash, ct);
            // stat 2 = Torrent preload; stat 3 = Torrent working; stat 4 = Torrent closed; stat 5 = In DB
            // Files appear once metadata is resolved (file_stat is non-empty)
            if (t?.Files is { Length: > 0 })
            {
                logger.LogDebug("[TorrServe] WaitForFiles hash={Hash} resolved {Count} files after {Attempts} polls",
                    hash, t.Files.Length, attempt);
                return t.Files;
            }
            logger.LogDebug("[TorrServe] WaitForFiles hash={Hash} attempt={Attempt} stat={Stat} ({StatStr}) files={Files}",
                hash, ++attempt, t?.Stat, t?.StatString, t?.Files?.Length ?? 0);
            await Task.Delay(2000, ct);
        }
        ct.ThrowIfCancellationRequested();
        return [];
    }

    /// <summary>Returns current torrent info including file list (null if not found).</summary>
    private async Task<TorrServeTorrent?> GetTorrentAsync(string hash, CancellationToken ct = default)
    {
        try
        {
            var result = await PostAsync<TorrentActionRequest, TorrServeTorrent>("/torrents", new TorrentActionRequest("get", hash),
                TorrServeJsonContext.Default.TorrentActionRequest,
                TorrServeJsonContext.Default.TorrServeTorrent, ct);
            logger.LogDebug("[TorrServe] GetTorrent hash={Hash} stat={Stat} ({StatStr}) files={Files}",
                hash, result.Stat, result.StatString, result.Files?.Length ?? 0);
            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("[TorrServe] GetTorrent hash={Hash} → not found: {Msg}", hash, ex.Message);
            return null;
        }
    }

    /// <summary>Returns the HTTP stream URL for a specific file index.</summary>
    private string GetStreamUrl(string hash, int fileIndex)
    {
        var url = $"{_baseUrl}/stream?link={hash}&index={fileIndex}&play";
        logger.LogDebug("[TorrServe] StreamUrl hash={Hash} index={Index} → {Url}", hash, fileIndex, url);
        return url;
    }

    private async Task<TRes> PostAsync<TReq, TRes>(
        string path, TReq body, JsonTypeInfo<TReq> bodyInfo, JsonTypeInfo<TRes> resultInfo, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, bodyInfo);
        logger.LogDebug("[TorrServe] POST {Url} ← {Body}", _baseUrl + path, json);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(_baseUrl + path, content, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        logger.LogDebug("[TorrServe] POST {Url} → {Status} {Body}",
            _baseUrl + path, (int)resp.StatusCode, respJson.Length > 200 ? respJson[..200] + "…" : respJson);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize(respJson, resultInfo)!;
    }
}