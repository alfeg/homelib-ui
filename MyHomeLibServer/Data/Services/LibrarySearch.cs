using Microsoft.EntityFrameworkCore;
using MyHomeLibServer.Data.Domain;

namespace MyHomeLibServer.Data.Services;

public class LibrarySearch
{
    private readonly ILogger<LibrarySearch> logger;

    public LibrarySearch(ILogger<LibrarySearch> logger)
    {
        this.logger = logger;
    }

    public IQueryable<Book> Search(string? term, LibDbContext db, CancellationToken token)
    {
        var query = db.Books
            .Include(b => b.Authors)
            .AsQueryable();
        
        if (term != null)
        {
            query = query
                .Where(b => b.Title.Contains(term));
        }
        // if (string.IsNullOrWhiteSpace(term))
        // {
        //     return db.BookItems.OrderByDescending(b => b.Date);
        // }
        //
        // var result =
        //     db.BookItems.FromSqlRaw(
        //         $"select b.* from books_fts bf join books b on bf.id = b.id where books_fts match '{term ?? ""}' order by RANK");

        return query; //new List<BookItem>().AsQueryable();
    }
}