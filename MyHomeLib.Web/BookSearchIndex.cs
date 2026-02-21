using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using MyHomeLib.Library;

namespace MyHomeLib.Web;

public sealed class BookSearchIndex : IAsyncDisposable
{
    private readonly DuckDBConnection _conn;
    private readonly IList<BookItem> _books; // index = DuckDB row id

    private BookSearchIndex(DuckDBConnection conn, IList<BookItem> books)
    {
        _conn = conn;
        _books = books;
    }

    public static async Task<BookSearchIndex> BuildAsync(IList<BookItem> books, ILogger? logger = null)
    {
        var conn = new DuckDBConnection("DataSource=:memory:");
        await conn.OpenAsync();

        await ExecAsync(conn, """
            CREATE TABLE books (
                id       INTEGER,
                title    VARCHAR NOT NULL,
                authors  VARCHAR NOT NULL,
                series   VARCHAR NOT NULL,
                keywords VARCHAR NOT NULL
            )
            """);

        using (var appender = conn.CreateAppender("books"))
        {
            for (int i = 0; i < books.Count; i++)
            {
                var book = books[i];
                var row = appender.CreateRow();
                row.AppendValue(i);
                row.AppendValue(book.Title ?? "");
                row.AppendValue(string.Join(", ", book.ParsedAuthors()));
                row.AppendValue(book.Series ?? "");
                row.AppendValue(book.Keywords ?? "");
                row.EndRow();
            }
        }

        // Russian Snowball stemmer; custom ignore keeps Cyrillic letters
        // (default '(\\.|[^a-z])+' would strip all Cyrillic)
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

        logger?.LogInformation("FTS index built for {Count} books", books.Count);

        return new BookSearchIndex(conn, books);
    }

    public async Task<(IReadOnlyList<BookItem> Page, int Total)> SearchAsync(string? query, int max = 200)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ([], 0);

        var sql = $"""
            SELECT id
            FROM (
                SELECT id, fts_main_books.match_bm25(id, {Literal(query)}) AS score
                FROM books
            )
            WHERE score IS NOT NULL
            ORDER BY score DESC
            """;

        var ids = new List<int>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt32(0));
        }

        var page = ids.Take(max)
            .Where(i => i >= 0 && i < _books.Count)
            .Select(i => _books[i])
            .ToList();

        return (page, ids.Count);
    }

    private static string Literal(string s) => $"'{s.Replace("'", "''")}'";

    private static async Task ExecAsync(DuckDBConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _conn.DisposeAsync();
}
