namespace MyHomeLib.Library;

public class BookItem : IBookItem
{
    public int Id { get; set; }

    /// <summary>Raw authors string in INPX format: Surname,FirstName,MiddleName:AnotherAuthor</summary>
    public string? Authors { get; set; }

    /// <summary>
    /// Parses <see cref="Authors"/> into individual display names ("FirstName MiddleName Surname").
    /// Empty name parts are omitted.
    /// </summary>
    public IReadOnlyList<string> ParsedAuthors()
    {
        if (string.IsNullOrWhiteSpace(Authors))
            return [];

        return Authors
            .Split(InpReader.AuthorDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(raw =>
            {
                var parts = raw.Split(',');
                var surname    = parts.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
                var firstName  = parts.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
                var middleName = parts.ElementAtOrDefault(2)?.Trim() ?? string.Empty;

                return string.Join(' ',
                    new[] { firstName, middleName, surname }
                    .Where(p => !string.IsNullOrEmpty(p)));
            })
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
    }

    /// <summary>Display form of <see cref="Authors"/>: "Ivan Petrov, Anna Sidorova".</summary>
    public string DisplayAuthors() => string.Join(", ", ParsedAuthors());

    /// <summary>Raw genre string in INPX format: colon-separated genre codes.</summary>
    public string? Genre { get; set; }

    /// <summary>Individual genre codes parsed out of <see cref="Genre"/>.</summary>
    public IReadOnlyList<string> ParsedGenreCodes()
    {
        if (string.IsNullOrWhiteSpace(Genre))
            return [];

        return Genre
            .Split(InpReader.GenreDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public string? Title { get; set; }

    public string? Series { get; set; }

    public string? SeriesNo { get; set; }

    public string? ArchiveFile { get; set; }
    public string? File { get; set; }

    public string? Ext { get; set; }

    /// <summary>Publication date from the INPX row. Null when absent or unparseable.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Year extracted from <see cref="Date"/>, for range filtering.</summary>
    public int? Year => Date?.Year;

    public long Size { get; set; }

    public string? Lang { get; set; }

    public bool Deleted { get; set; }

    public string? LibRate { get; set; }

    public string? Keywords { get; set; }
}
