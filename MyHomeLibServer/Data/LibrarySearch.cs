using MyHomeLib.Library;
using Microsoft.EntityFrameworkCore;

namespace MyHomeLibServer.Data;

public class LibrarySearch
{
    private readonly ILogger<LibrarySearch> logger;

    public LibrarySearch(ILogger<LibrarySearch> logger)
    {
        this.logger = logger;
    }

    public IQueryable<BookItem> Search(string? term, LibDbContext db, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return db.BookItems.OrderByDescending(b => b.Date);
        }

        var result =
            db.BookItems.FromSqlRaw(
                $"select b.* from books_fts bf join books b on bf.id = b.id where books_fts match '{term ?? ""}' order by RANK");

        return result;
    }
}