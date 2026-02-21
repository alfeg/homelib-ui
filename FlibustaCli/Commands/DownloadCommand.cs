using System.ComponentModel;
using MyHomeListServer.Torrent;
using Spectre.Console;
using Spectre.Console.Cli;

internal class DownloadCommand : AsyncCommand<DownloadCommand.DownloadCommandSettings>
{
    private readonly DownloadManager _downloadManager;
    private readonly LibraryIndexer _libraryIndexer;

    public class DownloadCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<id>")] public int BookId { get; set; }

        [CommandOption("-l|--library")]
        [Description("Specify what library to use for search")]
        public string? Library { get; set; }
    }

    public DownloadCommand(DownloadManager downloadManager, LibraryIndexer libraryIndexer)
    {
        _downloadManager = downloadManager;
        _libraryIndexer = libraryIndexer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DownloadCommandSettings settings, CancellationToken cancellationToken)
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

        foreach (var hash in LibraryHashes())
        {
            var books = await _libraryIndexer.ReadBooks(hash);

            var book = books.FirstOrDefault(b => b.Id == settings.BookId);

            if (book == null)
            {
                continue;
            }

            var response = await _downloadManager.DownloadFile(new DownloadRequest(hash, book.ArchiveFile, $"{book.File}.{book.Ext}"));
            var fileName = response.Name.ReplaceInvalidFileNameChars();
            await File.WriteAllBytesAsync(fileName, response.Data);
            AnsiConsole.MarkupLine($"Stored [bold]{fileName}[/] in \"{Path.GetFullPath(response.Name)}\"");
            return 0;
        }

        AnsiConsole.MarkupLine($"Cannot found book with id [bold]{settings.BookId}[/]");
        return 1;
    }
}