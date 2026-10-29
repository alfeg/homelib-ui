using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;

namespace MyHomeLib.Web.Services.TorrServe;

/// <summary>Raised when TorrServe already has all the work it is allowed. Callers answer 503.</summary>
public class TorrServeBusyException(string message) : Exception(message);

/// <summary>
/// Process-wide guard in front of TorrServe.
/// </summary>
/// <remarks>
/// TorrServe runs on a 1-CPU host and deadlocks under a burst: crawlers following /download
/// links drove it at ~60 downloads a minute, each one re-adding the torrent (a BBolt write
/// inside TorrServe) and polling it, until its API stopped answering for hours. Two things
/// keep that from recurring. Downloads are admitted a few at a time with a short bounded
/// queue, and overflow is refused at once instead of piling up behind a stuck server. And
/// the torrent's file list is remembered, so a warm download goes straight to the stream
/// without touching the torrent API at all.
/// </remarks>
public class TorrServeThrottle
{
    private readonly SemaphoreSlim _slots;
    private readonly int _capacity;
    private readonly TimeSpan _queueTimeout;
    private readonly TimeSpan _filesTtl;
    private readonly ConcurrentDictionary<string, (TorrServeFile[] Files, DateTime Expires)> _files =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Downloads running plus downloads waiting for a slot.</summary>
    private int _pending;

    public TorrServeThrottle(IOptions<TorrentConfig> config)
    {
        var c = config.Value;
        var concurrent = Math.Max(1, c.MaxConcurrentDownloads);
        _slots = new SemaphoreSlim(concurrent);
        _capacity = concurrent + Math.Max(0, c.MaxQueuedDownloads);
        _queueTimeout = TimeSpan.FromSeconds(Math.Max(1, c.QueueTimeoutSeconds));
        _filesTtl = TimeSpan.FromMinutes(Math.Max(0, c.FileListCacheMinutes));
    }

    /// <summary>Waits for a download slot. Dispose the result to give the slot back.</summary>
    /// <exception cref="TorrServeBusyException">The queue is full, or no slot freed up in time.</exception>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _pending) > _capacity)
        {
            Interlocked.Decrement(ref _pending);
            throw new TorrServeBusyException("TorrServe is busy with other downloads. Try again shortly.");
        }

        try
        {
            if (!await _slots.WaitAsync(_queueTimeout, ct))
                throw new TorrServeBusyException("TorrServe is busy with other downloads. Try again shortly.");
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            throw;
        }

        return new Lease(this);
    }

    public bool TryGetFiles(string hash, out TorrServeFile[] files)
    {
        if (_files.TryGetValue(hash, out var entry) && entry.Expires > DateTime.UtcNow)
        {
            files = entry.Files;
            return true;
        }

        files = [];
        return false;
    }

    public void RememberFiles(string hash, TorrServeFile[] files)
    {
        if (_filesTtl > TimeSpan.Zero)
            _files[hash] = (files, DateTime.UtcNow + _filesTtl);
    }

    public void ForgetFiles(string hash) => _files.TryRemove(hash, out _);

    private sealed class Lease(TorrServeThrottle owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            owner._slots.Release();
            Interlocked.Decrement(ref owner._pending);
        }
    }
}
