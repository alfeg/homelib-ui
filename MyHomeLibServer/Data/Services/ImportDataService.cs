using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using MyHomeLibServer.Core;
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
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(token);
            var sync = new SyncState
            {
                StartAt = DateTime.UtcNow,
                InpxFile = config.Value.CatalogIndexFile
            };
            sync.Etag = new FileInfo(sync.InpxFile).LastWriteTimeUtc.ToString();
            db.SyncStates.Add(sync);
            await db.SaveChangesAsync(token);
            var sw = Stopwatch.StartNew();
            var books = GetBooksStream(token);
            await StartWriterThread(books, token);

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

    private async IAsyncEnumerable<BookItem> GetBooksStream(CancellationToken cancellationToken)
    {
        var lib = library.Library;
        await Task.Yield();
        logger.LogInformation("Reading books from {catalog}", this.config.Value.CatalogIndexFile);

        var reader = new InpxReader();
        var duplicateChecker = new HashSet<long>();
        await foreach (var book in reader.ReadLibraryAsync(this.config.Value.CatalogIndexFile, lib)
                           .WithCancellation(cancellationToken))
        {
            lib.BooksRead++;
            if (duplicateChecker.Add(book.Id))
            {
                yield return book;
            }
        }
    }

    private async Task StartWriterThread(IAsyncEnumerable<BookItem> books, CancellationToken stoppingToken)
    {
        var lib = library.Library;

        await using (var dbCtx = await dbFactory.CreateDbContextAsync(stoppingToken))
        {
            await dbCtx.Database.ExecuteSqlRawAsync("delete from AuthorSeries;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("delete from AuthorBook;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("delete from Books;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("delete from Series;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("delete from Genre;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("delete from Keyword;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("delete from Authors;", cancellationToken: stoppingToken);
            await dbCtx.Database.ExecuteSqlRawAsync("drop table IF EXISTS books_fts;"
                                                    + " CREATE VIRTUAL TABLE books_fts USING fts5(title, authors, keywords, series, content='')",
                cancellationToken: stoppingToken);

            logger.LogInformation("Books database cleared");
            await dbCtx.SaveChangesAsync(stoppingToken);
        }

        Dictionary<string, int> authors = new();
        Dictionary<string, int> series = new();
        Dictionary<string, int> keywords = new();

        int authorIdSource = 0;
        int seriesId = 0;

        logger.LogInformation("Reading books info");
        var booksList = await books.ToListAsync(cancellationToken: stoppingToken);

        logger.LogInformation("Indexing books");
        var tracker = new ProgressTracker("Library indexing", booksList.Count, data =>
        {
            library.Library.IndexingMessage = string.Format(data.FormatTemplate, data.MessageArgs);
            logger.Log(LogLevel.Information, data.MessageTemplate, data.MessageArgs);
        });

        foreach (var chunk in booksList.Chunk(8000))
        {
            await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
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
                    serie = await db.Series.FindAsync(serieExistingId);
                }

                book.Series = serie;

                var authorsList = bookItem.Authors.Split(':', StringSplitOptions.RemoveEmptyEntries);

                foreach (var authorName in authorsList)
                {
                    Author author;
                    if (!authors.TryGetValue(authorName, out var authorId))
                    {
                        var splitName = authorName.Split(',');
                        author = new Author()
                        {
                            Id = ++authorIdSource,
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
                        author = await db.Authors.FindAsync(authorId);
                    }

                    book.Authors.Add(author);
                }

                db.Books.Add(book);
                db.BooksFts.Add(new BooksFts()
                {
                    RowId = book.Id,
                    Authors = bookItem.Authors,
                    Title = book.Title,
                    Keywords = bookItem.Keywords,
                    Series = bookItem.Series
                });

                lib.BooksAdded++;
                tracker.Track(1);
            }

            await db.SaveChangesAsync(stoppingToken);
        }
    }
}