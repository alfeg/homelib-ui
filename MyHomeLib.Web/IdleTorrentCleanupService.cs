using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyHomeListServer.Torrent;

namespace MyHomeLib.Web;

public sealed class IdleTorrentCleanupService(
    DownloadManager downloadManager,
    IOptions<LibraryConfig> config,
    ILogger<IdleTorrentCleanupService> logger) : BackgroundService
{
    private sealed record TrackedTorrent(string MagnetUri, DateTime LastActivityUtc);

    private readonly LibraryConfig _config = config.Value;
    private readonly ConcurrentDictionary<string, TrackedTorrent> _tracked = new(StringComparer.OrdinalIgnoreCase);

    public void MarkActivity(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
            return;

        try
        {
            var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
            _tracked.AddOrUpdate(
                hash,
                _ => new TrackedTorrent(magnetUri, DateTime.UtcNow),
                (_, existing) => existing with { MagnetUri = magnetUri, LastActivityUtc = DateTime.UtcNow });
        }
        catch (FormatException ex)
        {
            logger.LogDebug(ex, "Ignoring activity mark with invalid magnet URI.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sleepAfterMinutes = _config.TorrentSleepAfterMinutes;
                if (sleepAfterMinutes > 0)
                {
                    var idleThresholdUtc = DateTime.UtcNow.AddMinutes(-sleepAfterMinutes);

                    foreach (var entry in _tracked)
                    {
                        if (entry.Value.LastActivityUtc > idleThresholdUtc)
                            continue;

                        try
                        {
                            logger.LogInformation("[TorrServe] Torrent {Hash} idle for >= {Minutes} min — removing", entry.Key, sleepAfterMinutes);
                            await downloadManager.SleepLibraryAsync(entry.Value.MagnetUri, stoppingToken);
                            _tracked.TryRemove(entry.Key, out _);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to remove idle torrent {Hash} from TorrServe.", entry.Key);
                            _tracked.AddOrUpdate(
                                entry.Key,
                                _ => new TrackedTorrent(entry.Value.MagnetUri, DateTime.UtcNow),
                                (_, existing) => existing with { LastActivityUtc = DateTime.UtcNow });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Idle torrent cleanup loop failed, continuing.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
