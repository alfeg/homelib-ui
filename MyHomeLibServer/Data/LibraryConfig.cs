using Microsoft.Extensions.Options;
using MyHomeLib.Library;

namespace MyHomeLibServer.Data;

public class LibraryConfig
{
    public const string Section = "Library";

    public string CatalogIndexFile { get; set; }
}

public class LibraryReader : BackgroundService
{
    private readonly IOptions<LibraryConfig> config;
    private readonly LibraryAccessor library;

    public LibraryReader(IOptions<LibraryConfig> config, LibraryAccessor library)
    {
        this.config = config;
        this.library = library;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lib = await new InpxReader().ReadLibraryAsync(this.config.Value.CatalogIndexFile);
        library.SetLibrary(lib);
    }
}

public class LibrarySearch
{
    private readonly LibraryAccessor lib;

    public LibrarySearch(LibraryAccessor lib)
    {
        this.lib = lib;
    }

    public List<BookItem> Search(string title, string author)
    {
        IEnumerable<BookItem> query = lib.Library.BookItems;
        if (!string.IsNullOrWhiteSpace(title))
        {
            query = query.Where(x => x.Title.Contains(title));
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            query = query.Where(x => x.Authors.Any(a => a.Contains(author)));
        }

        return query
            .Take(100)
            .ToList();
    }
}

public class LibraryAccessor
{
    public InpxLibrary Library { get; private set; }

    public void SetLibrary(InpxLibrary inpxLibrary)
    {
        this.Library = inpxLibrary;
    }
}