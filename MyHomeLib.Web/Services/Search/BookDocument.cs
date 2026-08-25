using System.Text.Json.Serialization;
using MyHomeLib.Library;

namespace MyHomeLib.Web.Services.Search;

/// <summary>
/// One book as stored in Meilisearch. Field names are short and the set is minimal —
/// 586k documents means every extra field costs index size and RAM on a small VPS.
/// </summary>
public class BookDocument
{
    /// <summary>Primary key. Stable hash of archive+file+ext — see <see cref="BookUid"/>.</summary>
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    /// <summary>INPX library id. Displayed, not unique — do not key on it.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Display form, "Ivan Petrov, Anna Sidorova". Searchable.</summary>
    [JsonPropertyName("authors")]
    public string Authors { get; set; } = "";

    /// <summary>Individual author names, for faceting and exact filtering.</summary>
    [JsonPropertyName("authorList")]
    public string[] AuthorList { get; set; } = [];

    [JsonPropertyName("series")]
    public string Series { get; set; } = "";

    [JsonPropertyName("seriesNo")]
    public string SeriesNo { get; set; } = "";

    /// <summary>
    /// Numeric position within the series, for ordering. The raw INPX value is a string and
    /// carries junk ("0" for unnumbered, occasionally ranges), so anything unparseable
    /// becomes null and sorts last rather than pretending to be book zero.
    /// </summary>
    [JsonPropertyName("seriesIndex")]
    public int? SeriesIndex { get; set; }

    /// <summary>
    /// Folded copies of the text Meilisearch searches. Held separately from the display
    /// fields so a title still renders with its ё while queries match either spelling —
    /// see <see cref="RussianText"/>.
    /// </summary>
    [JsonPropertyName("titleSearch")]
    public string TitleSearch { get; set; } = "";

    [JsonPropertyName("authorsSearch")]
    public string AuthorsSearch { get; set; } = "";

    [JsonPropertyName("seriesSearch")]
    public string SeriesSearch { get; set; } = "";

    [JsonPropertyName("genres")]
    public string[] Genres { get; set; } = [];

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "";

    /// <summary>
    /// Year the book entered the collection (INPX date field), not the year it was written.
    /// Real collections have this populated for every row.
    /// </summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>
    /// Full date the book entered the collection, ISO yyyy-MM-dd. Same caveat as
    /// <see cref="Year"/>: this is an INPX bookkeeping date, not a publication date —
    /// the INPX carries no publication year at all.
    /// </summary>
    [JsonPropertyName("added")]
    public string? Added { get; set; }

    [JsonPropertyName("ext")]
    public string Ext { get; set; } = "";

    /// <summary>ZIP inside the torrent holding the book.</summary>
    [JsonPropertyName("archive")]
    public string Archive { get; set; } = "";

    /// <summary>Entry name inside the archive, without extension.</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    public static BookDocument From(BookItem book) => new()
    {
        Uid = BookUid.For(book),
        Id = book.Id,
        Title = book.Title ?? "",
        Authors = book.DisplayAuthors(),
        AuthorList = [.. book.ParsedAuthors()],
        Series = book.Series ?? "",
        TitleSearch = RussianText.Fold(book.Title),
        AuthorsSearch = RussianText.Fold(book.DisplayAuthors()),
        SeriesSearch = RussianText.Fold(book.Series),
        SeriesNo = book.SeriesNo ?? "",
        SeriesIndex = ParseSeriesIndex(book.SeriesNo),
        Genres = [.. book.ParsedGenreCodes()],
        Lang = book.Lang ?? "",
        Year = book.Year,
        Added = book.Date?.ToString("yyyy-MM-dd"),
        Ext = book.Ext ?? "",
        Archive = book.ArchiveFile ?? "",
        File = book.File ?? "",
        Size = book.Size,
    };

    /// <summary>Leading integer of the INPX seriesNo field; null when absent or zero.</summary>
    private static int? ParseSeriesIndex(string? seriesNo)
    {
        if (string.IsNullOrWhiteSpace(seriesNo)) return null;

        var digits = seriesNo.AsSpan().TrimStart();
        var end = 0;
        while (end < digits.Length && char.IsAsciiDigit(digits[end])) end++;
        if (end == 0) return null;

        return int.TryParse(digits[..end], out var parsed) && parsed > 0 ? parsed : null;
    }

    /// <summary>Series with its position, as readers expect to see it: "Name #3".</summary>
    public string SeriesDisplay() =>
        string.IsNullOrWhiteSpace(Series) ? ""
        : SeriesIndex is { } index ? $"{Series} #{index}"
        : Series;

    /// <summary>File name offered to the client: "Authors - Title.ext", sanitised.</summary>
    public string DownloadName()
    {
        var stem = string.IsNullOrWhiteSpace(Authors) ? Title : $"{Authors} - {Title}";
        if (string.IsNullOrWhiteSpace(stem)) stem = File;

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. stem.Select(c => invalid.Contains(c) ? '_' : c)]);

        // Long Cyrillic titles blow past filesystem limits on some readers.
        if (safe.Length > 120) safe = safe[..120].TrimEnd();

        return $"{safe}.{Ext}";
    }
}
