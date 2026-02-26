using System.IO.Compression;
using Fb2.Document;
using Microsoft.Extensions.Logging;

namespace MyHomeLib.Web;

public class DownloadManager(
    TorrServeClient torrServe,
    HttpClient httpClient,
    ILogger<DownloadManager> logger)
{
    /// <summary>Removes the library torrent from TorrServe to free resources (sleep mode).</summary>
    public async Task SleepLibraryAsync(string magnetUri, CancellationToken ct = default)
    {
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
        logger.LogInformation("[TorrServe] Removing library torrent {Hash} (idle sleep)", hash);
        await torrServe.RemoveTorrentAsync(hash, ct);
    }

    public async Task<SearchResponse?> SearchFiles(SearchRequest request, CancellationToken ct = default)
        => await SearchViaTorrServe(request, ct);

    public async Task<DownloadResponse?> DownloadFile(DownloadRequest request, CancellationToken ct = default)
        => await DownloadFileViaTorrServe(request, ct);

    // ── TorrServe paths ─────────────────────────────────────────────────────

    private async Task<SearchResponse> SearchViaTorrServe(SearchRequest request, CancellationToken ct)
    {
        var hash = request.Library;
        var magnetUri = request.MagnetUri
            ?? throw new InvalidOperationException("MagnetUri required for TorrServe search");

        await torrServe.AddTorrentAsync(magnetUri, hash, saveToDb: true, ct);
        var files = await torrServe.WaitForFilesAsync(hash, ct);
        var pattern = new WildcardPattern(request.FilePattern);
        var names = files.Select(f => f.Path).Where(p => pattern.IsMatch(p)).ToArray();
        return new SearchResponse(names);
    }

    private async Task<DownloadResponse> DownloadFileViaTorrServe(DownloadRequest request, CancellationToken ct)
    {
        var hash    = request.Library;
        var archive = request.Archive;
        var book    = request.Book;

        var magnetUri = request.MagnetUri
            ?? throw new InvalidOperationException("MagnetUri required for TorrServe download");

        logger.LogInformation("[TorrServe] [{Library}] {Name} Starting download", request.Library, request.Name);

        await torrServe.AddTorrentAsync(magnetUri, hash, saveToDb: true, ct);
        var files = await torrServe.WaitForFilesAsync(hash, ct);
        var file = files.FirstOrDefault(f => f.Path.EndsWith(archive))
                   ?? throw new FileNotFoundException($"Archive {archive} not found in torrent {hash}");

        var streamUrl = torrServe.GetStreamUrl(hash, file.Id);
        await using var archiveStream = new HttpRangeStream(httpClient, streamUrl, file.Length);

        var bookStream = new MemoryStream();
        if (book == null)
        {
            await archiveStream.CopyToAsync(bookStream, ct);
            return new DownloadResponse(bookStream.ToArray(), string.Empty, string.Empty);
        }

        logger.LogInformation("[TorrServe] [{Library}] {Name} Reading archive entry", request.Library, request.Name);
        using (var zip = new ZipArchive(archiveStream))
        {
            var entry = zip.GetEntry(book)
                        ?? throw new FileNotFoundException($"Entry {book} not found in {archive}");
            await entry.Open().CopyToAsync(bookStream, ct);
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
                logger.LogInformation("[TorrServe] [{Library}] Downloaded {Title}.fb2", request.Library, title);
                return new DownloadResponse(bookStream.ToArray(), "application/fb2", $"{title}.fb2");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[TorrServe] [{Library}] FB2 parsing failed for {Book}, serving raw file", request.Library, book);
                bookStream.Seek(0, SeekOrigin.Begin);
            }
        }

        logger.LogInformation("[TorrServe] [{Library}] Downloaded {Book}", request.Library, book);
        return new DownloadResponse(bookStream.ToArray(), string.Empty, book);
    }
}
