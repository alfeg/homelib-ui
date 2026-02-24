using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using MyHomeLib.Library;

namespace MyHomeLib.Web;

public sealed class BookSearchIndex : IAsyncDisposable
{
    private readonly DuckDBConnection _conn;

    private BookSearchIndex(DuckDBConnection conn) => _conn = conn;

    /// <summary>
    /// Opens or builds a file-backed DuckDB index.
    /// If the database file already contains books the INPX stream is not consumed at all.
    /// </summary>
    public static async Task<BookSearchIndex> BuildAsync(
        IAsyncEnumerable<BookItem> books,
        string dbPath,
        int duckDbMemoryLimitMb = 0,
        Action<string>? statusCallback = null,
        ILogger? logger = null)
    {
        var conn = new DuckDBConnection($"DataSource={dbPath}");
        await conn.OpenAsync();

        if (duckDbMemoryLimitMb > 0)
            await ExecAsync(conn, $"PRAGMA memory_limit='{duckDbMemoryLimitMb}MB'");

        // Reuse existing index if it already has books (e.g. on restart)
        long existing = 0;
        try
        {
            using var chk = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(*) FROM books";
            existing = (long)(await chk.ExecuteScalarAsync() ?? 0L);
        }
        catch { /* table not created yet */ }

        if (existing > 0)
        {
            await EnsureFtsIndexAsync(conn, statusCallback, logger);
            logger?.LogInformation("Reusing existing DuckDB index ({Count} books)", existing);
            statusCallback?.Invoke($"Loaded {existing:N0} books from existing index.");
            return new BookSearchIndex(conn);
        }

        await ExecAsync(conn, "DROP TABLE IF EXISTS books");
        await ExecAsync(conn, """
            CREATE TABLE books (
                id         INTEGER,
                authors    VARCHAR NOT NULL,
                genre      VARCHAR NOT NULL,
                title      VARCHAR NOT NULL,
                series     VARCHAR NOT NULL,
                series_no  VARCHAR NOT NULL,
                archive    VARCHAR NOT NULL,
                file       VARCHAR NOT NULL,
                ext        VARCHAR NOT NULL,
                date       TIMESTAMP,
                size       BIGINT,
                lang       VARCHAR NOT NULL,
                deleted    BOOLEAN,
                lib_rate   VARCHAR NOT NULL,
                keywords   VARCHAR NOT NULL
            )
            """);

        long count = 0;
        using (var appender = conn.CreateAppender("books"))
        {
            await foreach (var book in books)
            {
                var row = appender.CreateRow();
                row.AppendValue(book.Id);
                row.AppendValue(book.Authors    ?? "");
                row.AppendValue(book.Genre      ?? "");
                row.AppendValue(book.Title      ?? "");
                row.AppendValue(book.Series     ?? "");
                row.AppendValue(book.SeriesNo   ?? "");
                row.AppendValue(book.ArchiveFile ?? "");
                row.AppendValue(book.File       ?? "");
                row.AppendValue(book.Ext        ?? "");
                row.AppendValue(book.Date);
                row.AppendValue(book.Size);
                row.AppendValue(book.Lang       ?? "");
                row.AppendValue(book.Deleted);
                row.AppendValue(book.LibRate    ?? "");
                row.AppendValue(book.Keywords   ?? "");
                row.EndRow();
                count++;
                if (count % 50_000 == 0)
                    statusCallback?.Invoke($"Indexing… {count:N0} books");
            }
        }

        statusCallback?.Invoke("Building full-text search index…");
        await CreateFtsIndexAsync(conn);

        logger?.LogInformation("FTS index built for {Count} books", count);
        statusCallback?.Invoke($"Loaded {count:N0} books.");
        return new BookSearchIndex(conn);
    }

    public long TotalBooks
    {
        get
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM books";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    public async Task<IReadOnlyList<string>> GetLanguagesAsync()
    {
        var langs = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT lang FROM books WHERE lang IS NOT NULL AND trim(lang) <> '' ORDER BY lang";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            langs.Add(reader.GetString(0));
        return langs;
    }

    public async Task<(IReadOnlyList<BookItem> Page, int Total)> SearchAsync(string? query, string? language = null, int max = 200)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ([], 0);

        var languageFilter = string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : $" AND lower(lang) = lower({Literal(language.Trim())})";

        var sql = $"""
            SELECT id, authors, genre, title, series, series_no, archive, file, ext,
                   date, size, lang, deleted, lib_rate, keywords
            FROM (
                SELECT *, fts_main_books.match_bm25(id, {Literal(query)}) AS score
                FROM books
            )
            WHERE score IS NOT NULL{languageFilter}
            ORDER BY score DESC
            """;

        var all = new List<BookItem>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                all.Add(new BookItem
                {
                    Id          = reader.GetInt32(0),
                    Authors     = reader.IsDBNull(1)  ? "" : reader.GetString(1),
                    Genre       = reader.IsDBNull(2)  ? "" : reader.GetString(2),
                    Title       = reader.IsDBNull(3)  ? "" : reader.GetString(3),
                    Series      = reader.IsDBNull(4)  ? "" : reader.GetString(4),
                    SeriesNo    = reader.IsDBNull(5)  ? "" : reader.GetString(5),
                    ArchiveFile = reader.IsDBNull(6)  ? "" : reader.GetString(6),
                    File        = reader.IsDBNull(7)  ? "" : reader.GetString(7),
                    Ext         = reader.IsDBNull(8)  ? "" : reader.GetString(8),
                    Date        = reader.IsDBNull(9)  ? default : reader.GetDateTime(9),
                    Size        = reader.IsDBNull(10) ? 0   : reader.GetInt64(10),
                    Lang        = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    Deleted     = !reader.IsDBNull(12) && reader.GetBoolean(12),
                    LibRate     = reader.IsDBNull(13) ? "" : reader.GetString(13),
                    Keywords    = reader.IsDBNull(14) ? "" : reader.GetString(14),
                });
            }
        }

        return (all.Take(max).ToList(), all.Count);
    }

    private static string Literal(string s) => $"'{s.Replace("'", "''")}'";

    private static async Task EnsureFtsIndexAsync(
        DuckDBConnection conn,
        Action<string>? statusCallback,
        ILogger? logger)
    {
        var hasFts = false;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM duckdb_functions()
                WHERE function_name = 'match_bm25'
                  AND function_type = 'scalar'
                  AND schema_name = 'fts_main_books'
                """;
            hasFts = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L) > 0;
        }
        catch
        {
            hasFts = false;
        }

        if (hasFts)
            return;

        logger?.LogWarning("FTS index not found in existing books DB; rebuilding it now.");
        statusCallback?.Invoke("Rebuilding full-text search index…");
        await CreateFtsIndexAsync(conn);
    }

    private static async Task CreateFtsIndexAsync(DuckDBConnection conn)
    {
        // Russian Snowball stemmer; custom ignore keeps Cyrillic letters
        await ExecAsync(conn, @"
            PRAGMA create_fts_index(
                'books', 'id',
                'title', 'authors', 'series', 'keywords',
                stemmer    = 'russian',
                stopwords  = 'none',
                ignore     = '(\\.|[^a-zA-Zа-яёА-ЯЁ])+',
                lower      = 1,
                strip_accents = 1
            )");
    }

    private static async Task ExecAsync(DuckDBConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _conn.DisposeAsync();
}
