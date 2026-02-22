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
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

    public string? Genre { get; set; }

    public string? Title { get; set; }

    public string? Series { get; set; }

    public string? SeriesNo { get; set; }    

    public string? ArchiveFile { get; set; }
    public string? File { get; set; }
    
    public string? Ext { get; set; }
    public DateTime Date { get; set; }
    public long Size { get; set; }

    public string? Lang { get; set; }

    public bool Deleted { get; set; }
        
    public string? LibRate { get; set; }

    public string? Keywords { get; set; }
}
