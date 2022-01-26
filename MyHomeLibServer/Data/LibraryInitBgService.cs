using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;

namespace MyHomeLibServer.Data;

public class LibraryInitBgService : BackgroundService
{
    private readonly IOptions<LibraryConfig> config;
    private readonly LibraryAccessor library;
    private readonly IDbContextFactory<LibDbContext> dbFactory;

    public LibraryInitBgService(IOptions<LibraryConfig> config, LibraryAccessor library, IDbContextFactory<LibDbContext> dbFactory)
    {
        this.config = config;
        this.library = library;
        this.dbFactory = dbFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lib = new InpxLibrary();

        using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        await db.Database.ExecuteSqlRawAsync("delete from books;");
        await db.SaveChangesAsync(stoppingToken);

        var reader = new InpxReader();
        await foreach (var book in reader.ReadLibraryAsync(this.config.Value.CatalogIndexFile, lib))
        {
            await db.BookItems.AddAsync(book, stoppingToken);
        }

        await db.SaveChangesAsync(stoppingToken);

        library.SetLibrary(lib);
    }
}
