using System.Collections.Concurrent;
using System.Diagnostics;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using MyHomeLibServer.Data.Domain;

namespace MyHomeLibServer.Data.Services;

public class ImportDataService
{
    private readonly LibraryAccessor library;
    private readonly IOptions<LibraryConfig> config;
    private readonly ILogger<ImportDataService> logger;
    private readonly IDbContextFactory<LibDbContext> dbFactory;

    public ImportDataService(LibraryAccessor library, IOptions<LibraryConfig> config,
        IDbContextFactory<LibDbContext> dbContextFactory, ILogger<ImportDataService> logger)
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
        if (lastSync == null)
        {
            return true;
        }

        if (lastSync.IsSynced == false)
        {
            return true;
        }

        if (lastSync.InpxFile != config.Value.CatalogIndexFile)
        {
            return true;
        }

        if (lastSync.Etag != new FileInfo(lastSync.InpxFile).LastWriteTimeUtc.ToString())
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

            await Task.WhenAll(new[] {reader, writer});

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
                    // logger.LogInformation("Reading books from {catalog}. {count} read", this.config.Value.CatalogIndexFile, lib.BooksRead);
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
            await using (var db = await dbFactory.CreateDbContextAsync(stoppingToken))
            {
                await db.Database.ExecuteSqlRawAsync("delete from AuthorSeries;", cancellationToken: stoppingToken);
                await db.Database.ExecuteSqlRawAsync("delete from AuthorBook;", cancellationToken: stoppingToken);
                await db.Database.ExecuteSqlRawAsync("delete from Books;", cancellationToken: stoppingToken);
                await db.Database.ExecuteSqlRawAsync("delete from Series;", cancellationToken: stoppingToken);
                await db.Database.ExecuteSqlRawAsync("delete from Genre;", cancellationToken: stoppingToken);
                await db.Database.ExecuteSqlRawAsync("delete from Keyword;", cancellationToken: stoppingToken);
                await db.Database.ExecuteSqlRawAsync("delete from Authors;", cancellationToken: stoppingToken);

                logger.LogInformation("Books database cleared");
                await db.SaveChangesAsync(stoppingToken);
                //db.ChangeTracker.AutoDetectChangesEnabled = false;
            }

            logger.LogInformation("Indexing books");

            Dictionary<string, int> authors = new();
            Dictionary<string, int> series = new();
            Dictionary<string, int> keywords = new();

            int authorId = 0;
            int seriesId = 0;

            foreach (var chunk in lib.Queue.GetConsumingEnumerable().Chunk(1500))
            {
                await using (var db = await dbFactory.CreateDbContextAsync(stoppingToken))
                {
                    foreach (var bookItem in chunk)
                    {
                        var book = new Book()
                        {
                            Id = bookItem.Id,
                            Date = bookItem.Date,
                            Extension = bookItem.Ext,
                            Title = bookItem.Title,
                            Size = bookItem.Size,
                            ArchiveFile = bookItem.ArchiveFile,
                            FileName = bookItem.File,
                            Language = bookItem.Lang,
                            SeriesNo = int.TryParse(bookItem.SeriesNo, out var serno) ? serno : null
                        };

                        Series serie;
                        if (!series.TryGetValue(bookItem.Series, out var serieExistingId))
                        {
                            serie = new Series
                            {
                                Id = ++seriesId,
                                Name = bookItem.Series
                            };
                            series.Add(bookItem.Series, serie.Id);
                            db.Series.Add(serie);
                        }
                        else
                        {
                            serie = db.Series.Find(serieExistingId)!;
                        }

                        var authorsList = bookItem.Authors.Split(':', StringSplitOptions.RemoveEmptyEntries);

                        foreach (var authorName in authorsList)
                        {
                            Author author;
                            if (!authors.TryGetValue(authorName, out var authorExistingId))
                            {
                                var splitName = authorName.Split(',');
                                author = new Author()
                                {
                                    Id = ++authorId,
                                    LastName = splitName[0],
                                    FirstName = splitName.Length > 1 ? splitName[1] : String.Empty,
                                    MiddleName = splitName.Length > 2 ? splitName[2] : String.Empty,
                                };
                                authors.Add(authorName, author.Id);
                                db.Authors.Add(author);
                                author.Series.Add(serie);
                            }
                            else
                            {
                                author = db.Authors.Find(authorExistingId);
                            }

                            book.Authors.Add(author);
                        }

                        book.Series = serie;
                        db.Books.Add(book);
                        lib.BooksAdded++;
                        //
                        // if (lib.BooksAdded % 5000 == 0)
                        // {
                        //     logger.LogInformation("Indexing books. {count} completed", lib.BooksAdded);
                        //     await db.SaveChangesAsync(stoppingToken);
                        // }
                    }
                    logger.LogInformation("Indexing books. {count} completed", lib.BooksAdded);
                    await db.SaveChangesAsync(stoppingToken);
                }
            }

            // await db.Database.ExecuteSqlRawAsync(@"drop table IF EXISTS books_fts;
            //     CREATE VIRTUAL TABLE books_fts USING fts5(id, title, authors, keywords, series);
            //     insert into books_fts select id, title, authors, keywords, series from books ", cancellationToken: stoppingToken);
        });
    }
}