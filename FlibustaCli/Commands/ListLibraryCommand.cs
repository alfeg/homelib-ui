using MyHomeListServer.Torrent;
using Spectre.Console;
using Spectre.Console.Cli;

internal class ListLibraryCommand : AsyncCommand<ListLibrarySettings>
{
    private readonly LibraryIndexer _libraryIndexer;

    public ListLibraryCommand(LibraryIndexer libraryIndexer)
    {
        _libraryIndexer = libraryIndexer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ListLibrarySettings settings, CancellationToken cancellationToken)
    {
        var libraries = _libraryIndexer.ListLibraries()
            .OrderByDescending(l => l.Files.Count);

        var table = new Table();
        table.AddColumn("Hash");
        table.AddColumn("Name");
        table.AddColumn(new TableColumn("[bold]Books[/]").RightAligned());

        await AnsiConsole.Live(table).StartAsync(async ctx =>
        {
            foreach (var libary in libraries)
            {
                var hex = libary.InfoHashes.V1OrV2.ToHex();
                var books = await _libraryIndexer.ReadBooks(hex);
                table.AddRow(hex, libary.Name, $"[bold]{books.Count.ToString()}[/]");
                ctx.Refresh();
            }
        });
        return 0;
    }
}