using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;

namespace MyHomeLib.Web.Services;

public sealed class IdleTorrentCleanupService(
    IServiceProvider services,
    IMemoryCache cache,
    IOptions<TorrentConfig> config,
    ILogger<IdleTorrentCleanupService> logger)
{
    public void MarkActivity(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
            return;

        try
        {
            var minutes = config.Value.TorrentSleepAfterMinutes;
            if (minutes <= 0)
                return;

            var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
            cache.Set(hash, magnetUri, CreateEntryOptions(magnetUri));
        }
        catch (FormatException ex)
        {
            logger.LogDebug(ex, "Ignoring activity mark with invalid magnet URI");
        }
    }

    private MemoryCacheEntryOptions CreateEntryOptions(string magnetUri)
    {
        var options = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(config.Value.TorrentSleepAfterMinutes));

        options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = OnEvicted,
            State = magnetUri
        });

        return options;
    }

    private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (reason != EvictionReason.Expired)
            return;

        var magnetUri = (string)state!;
        var hash = (string)key;

        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("[TorrServe] Torrent {Hash} idle — removing", hash);
                await using var scope = services.CreateAsyncScope();
                var dm = scope.ServiceProvider.GetRequiredService<DownloadManager>();
                await dm.SleepLibraryAsync(magnetUri);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove idle torrent {Hash}, rescheduling", hash);
                cache.Set(hash, magnetUri, CreateEntryOptions(magnetUri));
            }
        });
    }
}
