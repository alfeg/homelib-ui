using MyHomeListServer.Torrent;
using Spectre.Console.Cli;

public class AddLibraryCommand : AsyncCommand<AddLibraryCommand.AddLibrarySettings>
{
    private readonly LibraryIndexer _libraryIndexer;

    public class AddLibrarySettings : LibraryCommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string? MagnetUri { get; set; }
    }

    public AddLibraryCommand(LibraryIndexer libraryIndexer)
    {
        _libraryIndexer = libraryIndexer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AddLibrarySettings settings, CancellationToken cancellationToken)
    {
        await _libraryIndexer.IndexLibrary(settings.MagnetUri ?? throw new ArgumentNullException(nameof(settings.MagnetUri)));
        return 0;
    }
}