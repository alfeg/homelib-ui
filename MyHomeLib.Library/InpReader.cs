namespace MyHomeLib.Library;

/// <summary>
/// Parses a single <c>.inp</c> entry from an INPX archive.
/// </summary>
/// <remarks>
/// Rows are newline separated (bare <c>\n</c> or <c>\r\n</c>); fields inside a row are
/// separated by <c>\x04</c>. Field indexes are fixed by the MyHomeLib INPX convention:
/// <code>
/// 0 authors  1 genre   2 title  3 series  4 seriesNo  5 file  6 size
/// 7 id       8 deleted 9 ext   10 date   11 lang     12 libRate 13 keywords
/// </code>
/// Parsing is deliberately tolerant: a malformed row is skipped rather than aborting the
/// whole collection. A single unparseable date in a 500k-row library must not cost the
/// entire index.
/// </remarks>
public static class InpReader
{
    public const char FieldDelimiter = '\x04';
    public const char AuthorDelimiter = ':';
    public const char GenreDelimiter = ':';

    private const int FieldAuthors = 0;
    private const int FieldGenre = 1;
    private const int FieldTitle = 2;
    private const int FieldSeries = 3;
    private const int FieldSeriesNo = 4;
    private const int FieldFile = 5;
    private const int FieldSize = 6;
    private const int FieldId = 7;
    private const int FieldDeleted = 8;
    private const int FieldExt = 9;
    private const int FieldDate = 10;
    private const int FieldLang = 11;
    private const int FieldLibRate = 12;
    private const int FieldKeywords = 13;

    /// <summary>
    /// Yields every book in one <c>.inp</c> entry. <paramref name="archiveFile"/> is the
    /// ZIP the books physically live in, derived from the entry name by the caller.
    /// </summary>
    public static IEnumerable<BookItem> ReadBooks(ReadOnlyMemory<byte> entryBytes, string archiveFile)
    {
        var text = TextDecoding.Decode(entryBytes.Span);
        return ReadBooks(text, archiveFile);
    }

    public static IEnumerable<BookItem> ReadBooks(string text, string archiveFile)
    {
        foreach (var line in EnumerateLines(text))
        {
            var book = ParseRow(line, archiveFile);
            if (book is not null) yield return book;
        }
    }

    /// <summary>Splits on \n, tolerating \r\n, without allocating an array of all lines.</summary>
    private static IEnumerable<string> EnumerateLines(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;

            var stop = end;
            if (stop > start && text[stop - 1] == '\r') stop--;

            if (stop > start) yield return text[start..stop];
            start = end + 1;
        }
    }

    /// <summary>Returns null when the row cannot yield a downloadable book.</summary>
    public static BookItem? ParseRow(string row, string archiveFile)
    {
        if (string.IsNullOrWhiteSpace(row)) return null;

        var fields = row.Split(FieldDelimiter);

        // Without a file name the entry cannot be located inside the archive, so the
        // row is useless regardless of how complete the rest of its metadata is.
        var file = Field(fields, FieldFile);
        if (string.IsNullOrWhiteSpace(file)) return null;

        return new BookItem
        {
            Id = ParseInt(Field(fields, FieldId)),
            Authors = Field(fields, FieldAuthors),
            Genre = Field(fields, FieldGenre),
            Title = Field(fields, FieldTitle),
            Series = Field(fields, FieldSeries),
            SeriesNo = Field(fields, FieldSeriesNo),
            File = file,
            Size = ParseLong(Field(fields, FieldSize)),
            Deleted = Field(fields, FieldDeleted) == "1",
            Ext = Field(fields, FieldExt).TrimStart('.'),
            Date = ParseDate(Field(fields, FieldDate)),
            Lang = Field(fields, FieldLang),
            LibRate = Field(fields, FieldLibRate),
            Keywords = Field(fields, FieldKeywords),
            ArchiveFile = archiveFile,
        };
    }

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index].Trim() : string.Empty;

    private static int ParseInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : 0;

    private static long ParseLong(string value) =>
        long.TryParse(value, out var parsed) ? parsed : 0;

    /// <summary>
    /// INPX dates are <c>yyyy-MM-dd</c>, but real collections contain blanks, zero dates
    /// and truncated values. Anything unparseable becomes null.
    /// </summary>
    private static DateOnly? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
