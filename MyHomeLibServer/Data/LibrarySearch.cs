using MyHomeLib.Library;

namespace MyHomeLibServer.Data;

public class LibrarySearch
{
    public List<BookItem> Search(string title, string author, LibDbContext db)
    {
        IEnumerable<BookItem> query = db.BookItems;
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
}
