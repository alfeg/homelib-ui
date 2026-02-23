using DuckDB.NET.Data;
using Microsoft.Extensions.Options;

namespace MyHomeLib.Web;

/// <summary>
/// Persists an audit trail of searches, download requests, and file downloads to DuckDB.
/// </summary>
public sealed class AuditService : IAsyncDisposable
{
    private readonly DuckDBConnection _db;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AuditService(IOptions<LibraryConfig> config)
    {
        var cfg = config.Value;
        var dbPath = !string.IsNullOrWhiteSpace(cfg.AuditDbPath)
            ? cfg.AuditDbPath
            : Path.Combine(cfg.DownloadsDirectory, "audit.db");

        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _db = new DuckDBConnection($"DataSource={dbPath}");
        _db.Open();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_log (
                id           VARCHAR PRIMARY KEY,
                event_type   VARCHAR NOT NULL,
                timestamp    TIMESTAMPTZ DEFAULT now(),
                query        VARCHAR,
                result_count INTEGER,
                book_id      INTEGER,
                title        VARCHAR,
                authors      VARCHAR,
                archive      VARCHAR,
                file_name    VARCHAR,
                job_id       VARCHAR
            )
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Logs a search query and its result count.</summary>
    public Task LogSearchAsync(string query, int resultCount) =>
        InsertAsync("Search", query: query, resultCount: resultCount);

    /// <summary>Logs a download being added to the queue.</summary>
    public Task LogEnqueueAsync(int bookId, string title, string authors, string archive, string fileName) =>
        InsertAsync("Enqueue", bookId: bookId, title: title, authors: authors, archive: archive, fileName: fileName);

    /// <summary>Logs a completed book file being served to the browser.</summary>
    public Task LogDownloadAsync(Guid jobId, string title, string fileName) =>
        InsertAsync("Download", title: title, fileName: fileName, jobId: jobId.ToString());

    private async Task InsertAsync(
        string eventType,
        string? query = null,
        int? resultCount = null,
        int? bookId = null,
        string? title = null,
        string? authors = null,
        string? archive = null,
        string? fileName = null,
        string? jobId = null)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO audit_log (id, event_type, query, result_count, book_id, title, authors, archive, file_name, job_id)
                VALUES (
                    '{Guid.NewGuid()}', '{eventType}',
                    {S(query)}, {N(resultCount)}, {N(bookId)},
                    {S(title)}, {S(authors)}, {S(archive)}, {S(fileName)}, {S(jobId)}
                )
                """;
            cmd.ExecuteNonQuery();
        }
        finally { _lock.Release(); }
    }

    private static string S(string? s) => s is null ? "NULL" : $"'{s.Replace("'", "''")}'";
    private static string N(int? n) => n is null ? "NULL" : n.Value.ToString();

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
