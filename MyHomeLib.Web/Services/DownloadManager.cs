using MyHomeLib.Web.Services.Models;
using MyHomeLib.Web.Services.TorrServe;

namespace MyHomeLib.Web.Services;

public class DownloadManager(
    TorrServeClient torrServe,
    ILogger<DownloadManager> logger)
{
    public async Task<(byte[] Data, string FileName)> GetInpxFileAsync(string magnetUri, CancellationToken ct)
    {
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);

        var searchResponse = await SearchFiles(
            new SearchRequest(hash, "*.inpx") { MagnetUri = magnetUri }, ct);

        var inpxPath = searchResponse?.Names.FirstOrDefault()
            ?? throw new InvalidOperationException("No *.inpx file found in the torrent.");

        var downloadResponse = await DownloadFile(
            new DownloadRequest(hash, inpxPath) { MagnetUri = magnetUri }, ct)
            ?? throw new InvalidOperationException("Failed to download INPX file.");

        return (downloadResponse.Data, Path.GetFileName(inpxPath));
    }

    /// <summary>Removes the library torrent from TorrServe to free resources (sleep mode).</summary>
    public async Task SleepLibraryAsync(string magnetUri, CancellationToken ct = default)
    {
        var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
        logger.LogInformation("[TorrServe] Removing library torrent {Hash} (idle sleep)", hash);
        await torrServe.RemoveTorrentAsync(hash, ct);
    }

    public Task<SearchResponse> SearchFiles(SearchRequest request, CancellationToken ct = default)
        => torrServe.Search(request, ct);

    public Task<DownloadResponse> DownloadFile(DownloadRequest request, CancellationToken ct = default)
        => torrServe.DownloadFile(request, ct);
}
