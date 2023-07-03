using System.ComponentModel;
using MyHomeLib.Library;
using MyHomeListServer.Torrent;
using Spectre.Console;
using Spectre.Console.Cli;

internal class SearchCommand : AsyncCommand<SearchCommand.SearchCommandSettings>
{
    private readonly LibraryIndexer _libraryIndexer;

    public class SearchCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<name>")] public string? Search { get; set; }

        [CommandOption("-l|--library")]
        [Description("Specify what library to use for search")]
        public string? Library { get; set; }

        [CommandOption("-m|--max")] public int MaxResults { get; set; } = 20;
    }

    public SearchCommand(LibraryIndexer libraryIndexer)
    {
        _libraryIndexer = libraryIndexer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, SearchCommandSettings settings)
    {
        IEnumerable<string> LibraryHashes()
        {
            if (settings.Library != null)
            {
                yield return settings.Library;
                yield break;
            }

            foreach (var library in _libraryIndexer.ListLibraries())
            {
                yield return library.InfoHashes.V1OrV2.ToHex();
            }
        }

        async IAsyncEnumerable<(string Library, BookItem Book)> SearchForBooks()
        {
            foreach (var library in LibraryHashes())
            {
                var results = _libraryIndexer.SearchLibrary(library, settings.Search);
                await foreach (var book in results)
                {
                    yield return (library, book);
                }
            }
        }

        var table = new Table();

        table.AddColumn("Hash");
        table.AddColumn("Book Title");
        table.AddColumn("Book Authors");
        table.AddColumn(new TableColumn("ID").RightAligned());

        await AnsiConsole.Live(table).StartAsync(async ctx =>
        {
            var count = 0;
            var max = Math.Min(settings.MaxResults, 100);
            await foreach (var result in SearchForBooks())
            {
                var library = result.Library;
                var book = result.Book;
                table.AddRow(library.Substring(0, 12), book.Title, book.Authors, book.Id.ToString());
                ctx.Refresh();
                if (++count >= max)
                {
                    break;
                }
            }
        });

        return 0;
    }
}