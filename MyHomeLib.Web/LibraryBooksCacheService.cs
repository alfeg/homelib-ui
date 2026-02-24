using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using MessagePack;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using MyHomeListServer.Torrent;

namespace MyHomeLib.Web;

public sealed class LibraryBooksCacheService(
    DownloadManager downloadManager,
    IOptions<LibraryConfig> config,
    ILogger<LibraryBooksCacheService> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheDirectory = ResolveCacheDirectory(config.Value.DownloadsDirectory);

    public async Task<LibraryBooksResponse> GetBooksAsync(string magnetUri, bool forceReindex, CancellationToken ct)
    {
        var cacheFiles = GetCacheFiles(magnetUri);

        if (!forceReindex)
        {
            var cached = await TryReadCacheAsync(cacheFiles.JsonPath, ct);
            if (cached is not null)
                return cached;
        }

        var gate = _locks.GetOrAdd(cacheFiles.Hash, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!forceReindex)
            {
                var cached = await TryReadCacheAsync(cacheFiles.JsonPath, ct);
                if (cached is not null)
                    return cached;
            }

            var indexed = await BuildIndexAsync(cacheFiles.Hash, magnetUri, ct);
            await WriteCacheArtifactsAtomicAsync(cacheFiles, indexed, ct);
            return indexed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<byte[]> GetBooksMsgPackBrAsync(string magnetUri, bool forceReindex, CancellationToken ct)
    {
        var cacheFiles = GetCacheFiles(magnetUri);

        if (!forceReindex && File.Exists(cacheFiles.MsgPackBrPath))
            return await File.ReadAllBytesAsync(cacheFiles.MsgPackBrPath, ct);

        var gate = _locks.GetOrAdd(cacheFiles.Hash, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!forceReindex && File.Exists(cacheFiles.MsgPackBrPath))
                return await File.ReadAllBytesAsync(cacheFiles.MsgPackBrPath, ct);

            LibraryBooksResponse response;
            if (!forceReindex)
            {
                response = await TryReadCacheAsync(cacheFiles.JsonPath, ct)
                    ?? await BuildIndexAsync(cacheFiles.Hash, magnetUri, ct);
            }
            else
            {
                response = await BuildIndexAsync(cacheFiles.Hash, magnetUri, ct);
            }

            await WriteCacheArtifactsAtomicAsync(cacheFiles, response, ct);
            return await File.ReadAllBytesAsync(cacheFiles.MsgPackBrPath, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LibraryBooksResponse?> TryReadCacheAsync(string cachePath, CancellationToken ct)
    {
        if (!File.Exists(cachePath))
            return null;

        try
        {
            await using var stream = File.OpenRead(cachePath);
            return await JsonSerializer.DeserializeAsync<LibraryBooksResponse>(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Cache file is corrupted and will be rebuilt: {Path}", cachePath);
            return null;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read cache file, rebuilding: {Path}", cachePath);
            return null;
        }
    }

    private async Task WriteCacheArtifactsAtomicAsync(CacheFiles cacheFiles, LibraryBooksResponse response, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var jsonTempPath = CreateTempPath(cacheFiles.JsonPath);
        var msgPackTempPath = CreateTempPath(cacheFiles.MsgPackPath);
        var msgPackBrTempPath = CreateTempPath(cacheFiles.MsgPackBrPath);

        try
        {
            await using (var stream = new FileStream(jsonTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, response, cancellationToken: ct);
            }

            var msgPackBytes = MessagePackSerializer.Serialize(response.ToMsgPack());
            await File.WriteAllBytesAsync(msgPackTempPath, msgPackBytes, ct);

            var compressedBytes = BrotliCompress(msgPackBytes);
            await File.WriteAllBytesAsync(msgPackBrTempPath, compressedBytes, ct);

            File.Move(jsonTempPath, cacheFiles.JsonPath, overwrite: true);
            File.Move(msgPackTempPath, cacheFiles.MsgPackPath, overwrite: true);
            File.Move(msgPackBrTempPath, cacheFiles.MsgPackBrPath, overwrite: true);
        }
        finally
        {
            TryDeleteTempFile(jsonTempPath);
            TryDeleteTempFile(msgPackTempPath);
            TryDeleteTempFile(msgPackBrTempPath);
        }
    }

    private CacheFiles GetCacheFiles(string magnetUri)
    {
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
        var normalizedHash = NormalizeHash(hash);

        return new CacheFiles(
            hash,
            Path.Combine(_cacheDirectory, $"library_{normalizedHash}.json"),
            Path.Combine(_cacheDirectory, $"library_{normalizedHash}.msgpack"),
            Path.Combine(_cacheDirectory, $"library_{normalizedHash}.msgpack.br"));
    }

    private static string ResolveCacheDirectory(string? downloadsDirectory)
    {
        var basePath = !string.IsNullOrWhiteSpace(downloadsDirectory)
            ? downloadsDirectory
            : AppContext.BaseDirectory;

        return Path.Combine(basePath, "app_data", "library_cache");
    }

    private static string NormalizeHash(string hash)
    {
        var normalized = new string(hash
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? "UNKNOWN" : normalized;
    }

    private async Task<LibraryBooksResponse> BuildIndexAsync(string hash, string magnetUri, CancellationToken ct)
    {
        logger.LogInformation("Indexing INPX for library {Hash}", hash);

        var searchResponse = await downloadManager.SearchFiles(
            new SearchRequest(hash, "*.inpx") { MagnetUri = magnetUri }, ct);

        var inpxPath = searchResponse?.Names.FirstOrDefault()
            ?? throw new InvalidOperationException("No *.inpx file found in the torrent.");

        var downloadResponse = await downloadManager.DownloadFile(
            new DownloadRequest(hash, inpxPath) { MagnetUri = magnetUri }, ct)
            ?? throw new InvalidOperationException("Failed to download INPX file.");

        var tempFile = Path.Combine(Path.GetTempPath(), $"mhl_{hash}_{Guid.NewGuid():N}.inpx");
        try
        {
            await File.WriteAllBytesAsync(tempFile, downloadResponse.Data, ct);

            var metadata = new InpxLibrary();
            var reader = new InpxReader();
            var books = new List<LibraryBookItem>();

            await foreach (var item in reader.ReadLibraryAsync(tempFile, metadata).WithCancellation(ct))
            {
                books.Add(new LibraryBookItem(
                    item.Id,
                    item.Title,
                    item.Authors,
                    item.Series,
                    item.SeriesNo,
                    item.Lang,
                    item.File,
                    item.Ext,
                    item.ArchiveFile));
            }

            return new LibraryBooksResponse(
                hash,
                new LibraryBooksMetadata(metadata.Description, metadata.Version, books.Count),
                books);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up temp INPX file {Path}", tempFile);
            }
        }
    }

    private static string CreateTempPath(string targetPath) =>
        Path.Combine(Path.GetDirectoryName(targetPath)!, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up temp cache file {Path}", tempPath);
        }
    }

    private static byte[] BrotliCompress(byte[] input)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(input, 0, input.Length);
        }

        return output.ToArray();
    }

    private sealed record CacheFiles(string Hash, string JsonPath, string MsgPackPath, string MsgPackBrPath);
}
