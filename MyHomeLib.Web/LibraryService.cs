using Microsoft.Extensions.Options;
using MyHomeLib.Library;

namespace MyHomeLib.Web;

public class LibraryService
{
    public Task<IList<BookItem>> LoadTask { get; }
    public InpxLibrary Metadata { get; } = new();

    public LibraryService(IOptions<LibraryConfig> config)
    {
        LoadTask = LoadAsync(config.Value.InpxPath, Metadata);
    }

    private static async Task<IList<BookItem>> LoadAsync(string path, InpxLibrary metadata)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "Library:InpxPath is not configured. " +
                "Pass it via appsettings.json, environment variable (Library__InpxPath), " +
                "or command-line argument (--Library:InpxPath=<path>).");

        var reader = new InpxReader();
        var books = new List<BookItem>();
        await foreach (var book in reader.ReadLibraryAsync(path, metadata))
            books.Add(book);
        return books;
    }

    public async Task<(IReadOnlyList<BookItem> Page, int Total)> SearchAsync(
        string? title, string? author, string? series, int max = 200)
    {
        var books = await LoadTask;

        IEnumerable<BookItem> query = books;

        if (!string.IsNullOrWhiteSpace(title))
            query = query.Where(b => b.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(author))
            query = query.Where(b => b.Authors.Contains(author, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(series))
            query = query.Where(b => b.Series != null &&
                                     b.Series.Contains(series, StringComparison.OrdinalIgnoreCase));

        var all = query.ToList();
        return (all.Take(max).ToList(), all.Count);
    }
}
