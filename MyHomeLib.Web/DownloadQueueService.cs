using System.Collections.Concurrent;
using System.Threading.Channels;
using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MyHomeLib.Library;
using MyHomeListServer.Torrent;

namespace MyHomeLib.Web;

public sealed class DownloadQueueService : BackgroundService, IAsyncDisposable
{
    private readonly DownloadManager _downloadManager;
    private readonly LibraryConfig _config;
    private readonly ILogger<DownloadQueueService> _logger;
    private readonly DuckDBConnection _db;
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TorrentStats> _activeStats = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobCts = new();

    public bool IsEnabled => _config.TorrentEnabled;

    /// <summary>Live torrent stats for jobs currently downloading.</summary>
    public IReadOnlyDictionary<Guid, TorrentStats> ActiveStats => _activeStats;

    public DownloadQueueService(
        DownloadManager downloadManager,
        IOptions<LibraryConfig> config,
        ILogger<DownloadQueueService> logger)
    {
        _downloadManager = downloadManager;
        _config = config.Value;
        _logger = logger;

        var dbPath = string.IsNullOrWhiteSpace(_config.QueueDbPath)
            ? Path.Combine(_config.DownloadsDirectory, "queue.db")
            : _config.QueueDbPath;

        var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);

        _db = new DuckDBConnection($"DataSource={dbPath}");
        _db.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS download_queue (
                id            VARCHAR PRIMARY KEY,
                book_id       INTEGER,
                title         VARCHAR,
                authors       VARCHAR,
                archive       VARCHAR,
                file_name     VARCHAR,
                status        VARCHAR DEFAULT 'Pending',
                error         VARCHAR,
                file_path     VARCHAR,
                download_name VARCHAR,
                content_type  VARCHAR,
                added_at      TIMESTAMP DEFAULT now(),
                completed_at  TIMESTAMP
            )
            """);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Torrent downloads disabled: Library:MagnetUri or Library:DownloadsDirectory not set.");
            return;
        }

        Directory.CreateDirectory(_config.DownloadsDirectory);

        // Reset jobs that were interrupted mid-download back to Pending
        await DbExecAsync("UPDATE download_queue SET status = 'Pending' WHERE status = 'Downloading'");

        // Re-enqueue all Pending jobs from the last run
        foreach (var id in await GetIdsByStatusAsync("Pending"))
            _channel.Writer.TryWrite(id);

        await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App is shutting down — leave job as Pending so it restarts next time
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing job {JobId}", jobId);
            }
        }
    }

    public async Task<Guid> EnqueueAsync(MyHomeLib.Library.BookItem book)
    {
        // Deduplicate by archive + file_name
        var existing = await ScalarAsync<string>(
            $"SELECT id FROM download_queue WHERE archive = '{Esc(book.ArchiveFile)}' AND file_name = '{Esc(book.File + "." + book.Ext)}' AND status <> 'Failed' LIMIT 1");
        if (existing != null)
            return Guid.Parse(existing);

        var job = new DownloadJob
        {
            BookId   = book.Id,
            Title    = book.Title ?? string.Empty,
            Authors  = string.Join(", ", book.ParsedAuthors()),
            Archive  = book.ArchiveFile ?? string.Empty,
            FileName = $"{book.File}.{book.Ext}",
        };

        await DbExecAsync($"""
            INSERT INTO download_queue (id, book_id, title, authors, archive, file_name)
            VALUES ('{job.Id}', {job.BookId}, '{Esc(job.Title)}', '{Esc(job.Authors)}',
                    '{Esc(job.Archive)}', '{Esc(job.FileName)}')
            """);

        _channel.Writer.TryWrite(job.Id);
        return job.Id;
    }

    public async Task<IReadOnlyList<DownloadJob>> GetAllAsync()
    {
        var jobs = new List<DownloadJob>();
        await _dbLock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id, book_id, title, authors, archive, file_name, status, error, file_path, download_name, content_type, added_at, completed_at FROM download_queue ORDER BY added_at DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                jobs.Add(new DownloadJob
                {
                    Id           = Guid.Parse(reader.GetString(0)),
                    BookId       = reader.GetInt32(1),
                    Title        = reader.IsDBNull(2)  ? "" : reader.GetString(2),
                    Authors      = reader.IsDBNull(3)  ? "" : reader.GetString(3),
                    Archive      = reader.IsDBNull(4)  ? "" : reader.GetString(4),
                    FileName     = reader.IsDBNull(5)  ? "" : reader.GetString(5),
                    Status       = Enum.Parse<DownloadStatus>(reader.IsDBNull(6) ? "Pending" : reader.GetString(6)),
                    Error        = reader.IsDBNull(7)  ? null : reader.GetString(7),
                    FilePath     = reader.IsDBNull(8)  ? null : reader.GetString(8),
                    DownloadName = reader.IsDBNull(9)  ? null : reader.GetString(9),
                    ContentType  = reader.IsDBNull(10) ? null : reader.GetString(10),
                    AddedAt      = reader.GetDateTime(11),
                    CompletedAt  = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                });
            }
        }
        finally { _dbLock.Release(); }
        return jobs;
    }

    public async Task DeleteAsync(Guid jobId)
    {
        // Cancel active download if running (fire-and-forget the cancellation)
        if (_jobCts.TryRemove(jobId, out var cts))
            _ = cts.CancelAsync();

        // Delete from DB and disk immediately — don't wait for ProcessJobAsync
        var job = (await GetAllAsync()).FirstOrDefault(j => j.Id == jobId);
        if (job?.FilePath != null && File.Exists(job.FilePath))
            File.Delete(job.FilePath);

        await DbExecAsync($"DELETE FROM download_queue WHERE id = '{jobId}'");
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        // Per-job CTS linked to app shutdown, so the user can cancel individual jobs
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _jobCts[jobId] = jobCts;

        await DbExecAsync($"UPDATE download_queue SET status = 'Downloading' WHERE id = '{jobId}'");
        _logger.LogInformation("Processing download job {JobId}", jobId);

        try
        {
            var job = (await GetAllAsync()).FirstOrDefault(j => j.Id == jobId);
            if (job is null) return;

            var link = MagnetLink.Parse(_config.MagnetUri);
            var hash = link.InfoHashes.V1OrV2.ToHex();

            var progress = new Progress<TorrentStats>(s => _activeStats[jobId] = s);
            var request = new DownloadRequest(hash, job.Archive, job.FileName) { Link = link, Progress = progress };
            var response = await _downloadManager.DownloadFile(request, jobCts.Token);

            var safeName = MakeSafeFileName(response!.Name.Length > 0 ? response.Name : job.FileName);
            var filePath = Path.Combine(_config.DownloadsDirectory, safeName);

            var counter = 1;
            while (File.Exists(filePath))
            {
                var ext = Path.GetExtension(safeName);
                filePath = Path.Combine(_config.DownloadsDirectory,
                    $"{Path.GetFileNameWithoutExtension(safeName)}_{counter++}{ext}");
            }

            await File.WriteAllBytesAsync(filePath, response.Data, stoppingToken);

            await DbExecAsync($"""
                UPDATE download_queue
                SET status = 'Ready', file_path = '{Esc(filePath)}',
                    download_name = '{Esc(safeName)}',
                    content_type  = '{Esc(response.ContentType)}',
                    completed_at  = now()
                WHERE id = '{jobId}'
                """);

            _logger.LogInformation("Job {JobId} completed → {FilePath}", jobId, filePath);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // User-initiated abort — DeleteAsync already removed from DB; this is a no-op safety net
            _logger.LogInformation("Job {JobId} aborted by user", jobId);
            await DbExecAsync($"DELETE FROM download_queue WHERE id = '{jobId}'");
        }
        catch (OperationCanceledException)
        {
            // App shutdown: leave as Pending so it restarts next time
            await DbExecAsync($"UPDATE download_queue SET status = 'Pending' WHERE id = '{jobId}'");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            await DbExecAsync($"""
                UPDATE download_queue
                SET status = 'Failed', error = '{Esc(ex.Message)}', completed_at = now()
                WHERE id = '{jobId}'
                """);
        }
        finally
        {
            _activeStats.TryRemove(jobId, out _);
            _jobCts.TryRemove(jobId, out _);
        }
    }

    private async Task<List<Guid>> GetIdsByStatusAsync(string status)
    {
        var ids = new List<Guid>();
        await _dbLock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"SELECT id FROM download_queue WHERE status = '{status}' ORDER BY added_at";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(Guid.Parse(reader.GetString(0)));
        }
        finally { _dbLock.Release(); }
        return ids;
    }

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await _dbLock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            var result = cmd.ExecuteScalar();
            return result is DBNull or null ? default : (T)result;
        }
        finally { _dbLock.Release(); }
    }

    private async Task DbExecAsync(string sql)
    {
        await _dbLock.WaitAsync();
        try { Exec(sql); }
        finally { _dbLock.Release(); }
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "''");

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await _db.DisposeAsync();
    }
}
