using System.IO.Compression;

namespace MyHomeLib.Library;

public class InpxLibrary
{
    public List<BookItem> BookItems { get; } = new List<BookItem>();
    public string Description { get; set; }
    public string Version { get; set; }
    public string IndexFilePath { get; set; }
    public string LibraryFolder { get; set; }

    Dictionary<long, BookItem> Index = new();

    public void AddBook(BookItem book)
    {
        BookItems.Add(book);
        book.Id = BookItems.Count;
        Index.Add(book.Id, book);
    }
    
    public Stream OpenBook(long libId, out BookItem book)
    {
        book = Index[libId];
        var pathToFile = Path.Combine(LibraryFolder, book.ArchiveFile);
        using var file = File.OpenRead(pathToFile);
        using var zip = new ZipArchive(file);
        var entry = zip.GetEntry(book.File + "." + book.Ext);
        var ms = new MemoryStream();
        using var fb2 = entry.Open();
        fb2.CopyTo(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
    
}
