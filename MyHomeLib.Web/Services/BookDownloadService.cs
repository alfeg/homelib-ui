using System.IO.Compression;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services.Models;
using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web.Services;

public record BookPayload(byte[] Data, string ContentType, string FileName);

/// <summary>
/// Fetches one book out of the library torrent. The magnet comes from configuration and
/// never from the request — that is the difference between a private library server and
/// an open torrent relay.
/// </summary>
public class BookDownloadService(
    DownloadManager downloads,
    IOptions<LibraryConfig> libraryConfig,
    ILogger<BookDownloadService> logger)
{
    public const string Fb2ContentType = "application/fb2+xml";
    public const string Fb2ZipContentType = "application/fb2+zip";

    private readonly LibraryConfig _config = libraryConfig.Value;

    /// <summary>True when a magnet is configured, i.e. downloads are possible at all.</summary>
    public bool CanDownload => !string.IsNullOrWhiteSpace(_config.MagnetUri);

    public async Task<BookPayload> DownloadAsync(BookDocument book, bool zipped, CancellationToken ct)
    {
        if (!CanDownload)
        {
            throw new InvalidOperationException(
                "This instance has no Library:MagnetUri configured, so books cannot be fetched. " +
                "Search works from the local index only.");
        }

        var magnetUri = _config.MagnetUri!;
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
        var entryName = $"{book.File}.{book.Ext}";

        logger.LogInformation("[Download] {Uid} {Archive}::{Entry}", book.Uid, book.Archive, entryName);

        var response = await downloads.DownloadFile(
            new DownloadRequest(hash, book.Archive, entryName) { MagnetUri = magnetUri }, ct);

        var fileName = book.DownloadName();

        if (!zipped)
        {
            var contentType = book.Ext.Equals("fb2", StringComparison.OrdinalIgnoreCase)
                ? Fb2ContentType
                : "application/octet-stream";

            return new BookPayload(response.Data, contentType, fileName);
        }

        return new BookPayload(Zip(response.Data, fileName), Fb2ZipContentType, $"{fileName}.zip");
    }

    /// <summary>
    /// Wraps the book in a ZIP. Several e-reader OPDS clients — PocketBook among them —
    /// expect fb2 delivered as fb2+zip, so both variants are offered per book.
    /// </summary>
    private static byte[] Zip(byte[] data, string entryName)
    {
        using var buffer = new MemoryStream(data.Length / 2);

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(data, 0, data.Length);
        }

        return buffer.ToArray();
    }
}
