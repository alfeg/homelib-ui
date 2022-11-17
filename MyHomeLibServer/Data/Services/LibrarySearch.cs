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
         return db.BooksFts
            .Include(b => b.Book)
            .Include(b => b.Book.Authors)
            .Include(b => b.Book.Series)
            .Where(b => b.Match == (term ?? ""))
            .OrderBy(b => b.Rank)
            .Select(b => b.Book);


        // if (string.IsNullOrWhiteSpace(term))
        // {
        //     return db.Books.OrderByDescending(b => b.Date);
        // }
        //
        // var result =
        //     db.Books.FromSqlInterpolated(
        //         $"select b.* from books_fts bf join books b on bf.rowid = b.id where books_fts match {term ?? ""} order by RANK")
        //     .Include(b => b.Authors)
        //     .Include(b => b.Series);
        //
        // return result; //new List<BookItem>().AsQueryable();
    }
}