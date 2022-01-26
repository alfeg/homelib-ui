using MyHomeLib.Library;
using System.Diagnostics;

namespace MyHomeLibServer.Data;

public class LibrarySearch
{
    private readonly ILogger<LibrarySearch> logger;

    public LibrarySearch(ILogger<LibrarySearch> logger)
    {
        this.logger = logger;
    }

    public List<BookItem> Search(string title, string author, LibDbContext db)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            IQueryable<BookItem> query = db.BookItems;
            if (!string.IsNullOrWhiteSpace(title))
            {
                query = query.Where(x => x.Title.Contains(title));
            }

            if (!string.IsNullOrWhiteSpace(author))
            {
                query = query.Where(x => x.Authors.Contains(author));
            }

            return query
                .Take(50)
                .ToList();
        }
        finally
        {
            sw.Stop();
            logger.LogInformation("Search completed in {time}ms", sw.Elapsed.TotalMilliseconds);
        }
    }
}
