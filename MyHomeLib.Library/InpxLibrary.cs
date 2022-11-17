using System.Collections.Concurrent;
using System.IO.Compression;

namespace MyHomeLib.Library;

public class InpxLibrary
{
    public bool IsIndexing { get; set; }
    public long BooksAdded { get; set; }
    public long BooksRead { get; set; }
    public string Description { get; set; }
    public string Version { get; set; }
    public string IndexFilePath { get; set; }
    public string LibraryFolder { get; set; }
    
    public string IndexingMessage { get; set; }

    public Stream OpenBook(IBookItem book)
    {        
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
