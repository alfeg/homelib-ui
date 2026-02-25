using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyHomeLib.Torrent;
using MyHomeListServer.Torrent;

namespace MyHomeLib.Web;

public sealed class LibraryBooksCacheService(
    DownloadManager downloadManager,
    IOptions<LibraryConfig> config,
    ILogger<LibraryBooksCacheService> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheDirectory = ResolveCacheDirectory(config.Value.DownloadsDirectory);

    public async Task<(byte[] Data, string FileName)> GetInpxFileAsync(string magnetUri, CancellationToken ct)
    {
        var cacheFiles = GetCacheFiles(magnetUri);

        var cached = await TryReadCachedBytesAsync(cacheFiles.InpxPath, ct);
        if (cached is not null)
            return (cached, Path.GetFileName(cacheFiles.InpxPath));

        var gate = _locks.GetOrAdd(cacheFiles.Hash, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            cached = await TryReadCachedBytesAsync(cacheFiles.InpxPath, ct);
            if (cached is not null)
                return (cached, Path.GetFileName(cacheFiles.InpxPath));

            var inpxData = await DownloadInpxAsync(cacheFiles.Hash, magnetUri, ct);
            await WriteBytesAtomicAsync(cacheFiles.InpxPath, inpxData, ct);
            return (inpxData, Path.GetFileName(cacheFiles.InpxPath));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<byte[]?> TryReadCachedBytesAsync(string cachePath, CancellationToken ct)
    {
        if (!File.Exists(cachePath))
            return null;

        try
        {
            return await File.ReadAllBytesAsync(cachePath, ct);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read cache file, rebuilding: {Path}", cachePath);
            return null;
        }
    }

    private CacheFiles GetCacheFiles(string magnetUri)
    {
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
        var normalizedHash = NormalizeHash(hash);

        return new CacheFiles(
            hash,
            Path.Combine(_cacheDirectory, $"library_{normalizedHash}.inpx"));
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

    private async Task<byte[]> DownloadInpxAsync(string hash, string magnetUri, CancellationToken ct)
    {
        var searchResponse = await downloadManager.SearchFiles(
            new SearchRequest(hash, "*.inpx") { MagnetUri = magnetUri }, ct);

        var inpxPath = searchResponse?.Names.FirstOrDefault()
            ?? throw new InvalidOperationException("No *.inpx file found in the torrent.");

        var downloadResponse = await downloadManager.DownloadFile(
            new DownloadRequest(hash, inpxPath) { MagnetUri = magnetUri }, ct)
            ?? throw new InvalidOperationException("Failed to download INPX file.");

        return downloadResponse.Data;
    }

    private async Task WriteBytesAtomicAsync(string targetPath, byte[] data, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var tempPath = CreateTempPath(targetPath);
        try
        {
            await File.WriteAllBytesAsync(tempPath, data, ct);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
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

    private sealed record CacheFiles(string Hash, string InpxPath);
}
