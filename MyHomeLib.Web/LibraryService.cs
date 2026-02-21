using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;

namespace MyHomeLib.Web;

public class LibraryService : IAsyncDisposable
{
    public Task<IList<BookItem>> LoadTask { get; }
    public Task<BookSearchIndex> IndexTask { get; }
    public InpxLibrary Metadata { get; } = new();

    public LibraryService(IOptions<LibraryConfig> config, ILogger<LibraryService> logger)
    {
        LoadTask = LoadAsync(config.Value.InpxPath, Metadata);
        IndexTask = BuildIndexAsync(logger);
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

    private async Task<BookSearchIndex> BuildIndexAsync(ILogger logger)
    {
        var books = await LoadTask;
        return await BookSearchIndex.BuildAsync(books, logger);
    }

    public async Task<(IReadOnlyList<BookItem> Page, int Total)> SearchAsync(string? query, int max = 200)
    {
        var index = await IndexTask;
        return await index.SearchAsync(query, max);
    }

    public async ValueTask DisposeAsync()
    {
        if (IndexTask.IsCompletedSuccessfully)
            await IndexTask.Result.DisposeAsync();
    }
}
