using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MyHomeLibServer.Data;

public class ImportDataService
{
    private readonly LibraryAccessor library;
    private readonly IOptions<LibraryConfig> config;
    private readonly ILogger<ImportDataService> logger;
    private readonly IDbContextFactory<LibDbContext> dbFactory;

    public ImportDataService(LibraryAccessor library, IOptions<LibraryConfig> config, IDbContextFactory<LibDbContext> dbContextFactory, ILogger<ImportDataService> logger)
    {
        this.library = library;
        this.config = config;
        this.logger = logger;
        this.dbFactory = dbContextFactory;
    }

    public async Task<bool> IsSyncRequired()
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var lastSync = await db.SyncStates.OrderByDescending(s => s.Id).FirstOrDefaultAsync();
        if(lastSync == null)
        {
            return true;
        }

        if(lastSync.IsSynced == false)
        {
            return true;
        }

        if(lastSync.InpxFile != config.Value.CatalogIndexFile)
        {
            return true;
        }

        if(lastSync.Etag != new FileInfo(lastSync.InpxFile).LastWriteTimeUtc.ToString())
        {
            return true;
        }

        return false;
    }

    public async Task SyncDataAsync(CancellationToken token)
    {
        var lib = library.Library;

        if (lib.IsIndexing) return;

        lib.IsIndexing = true;
        lib.Queue = new BlockingCollection<BookItem>(50000);

        try
        {
            using var db = dbFactory.CreateDbContext();
            var sync = new SyncState();
            sync.StartAt = DateTime.UtcNow;
            sync.InpxFile = config.Value.CatalogIndexFile;
            sync.Etag = new FileInfo(sync.InpxFile).LastWriteTimeUtc.ToString();
            db.SyncStates.Add(sync);
            await db.SaveChangesAsync(token);
            var sw = Stopwatch.StartNew();
            var reader = StartReaderThread();
            var writer = StartWriterThread(token);

            await Task.WhenAll(new[] { reader, writer });            

            sync.EndAt = DateTime.UtcNow;
            sync.DurationMs = sw.ElapsedMilliseconds;
            sync.IsSynced = true;
            await db.SaveChangesAsync(token);
        }
        finally
        {
            lib.IsIndexing = false;
        }

        await Task.Yield();
    }

    private Task StartReaderThread()
    {
        return Task.Run(async () =>
        {
            var lib = library.Library;
            await Task.Yield();
            logger.LogInformation("Reading books from {catalog}", this.config.Value.CatalogIndexFile);

            var reader = new InpxReader();
            var duplicateChecker = new HashSet<long>();
            await foreach (var book in reader.ReadLibraryAsync(this.config.Value.CatalogIndexFile, lib))
            {
                lib.BooksRead++;
                if (lib.BooksRead % 1000 == 0)
                {
                    logger.LogInformation("Reading books from {catalog}. {count} read", this.config.Value.CatalogIndexFile, lib.BooksRead);
                    await Task.Yield();
                }

                if (duplicateChecker.Add(book.Id))
                {
                    lib.Queue.Add(book);
                }
            }

            lib.Queue.CompleteAdding();
        });
    }

    private Task StartWriterThread(CancellationToken stoppingToken)
    {
        return Task.Run(async () =>
        {
            var lib = library.Library;
            await Task.Yield();
            using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
            await db.Database.ExecuteSqlRawAsync("delete from books;");
            logger.LogInformation("Books database cleared");
            await db.SaveChangesAsync(stoppingToken);
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            logger.LogInformation("Indexing books");

            foreach (var book in lib.Queue.GetConsumingEnumerable())
            {
                await db.BookItems.AddAsync(book, stoppingToken);
                lib.BooksAdded++;

                if (lib.BooksAdded % 5000 == 0)
                {
                    logger.LogInformation("Indexing books. {count} completed", lib.BooksAdded);
                    await db.SaveChangesAsync(stoppingToken);
                }
            }

            await db.SaveChangesAsync(stoppingToken);
        });
    }
}
