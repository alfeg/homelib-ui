using System.IO.Compression;

namespace MyHomeLib.Library;

public class InpxReader
{
    public async IAsyncEnumerable<BookItem> ReadLibraryAsync(string indexFile, InpxLibrary library)
    {
        library.IndexFilePath = indexFile;
        library.LibraryFolder = Path.GetDirectoryName(indexFile)!;
        
        using var file = File.OpenRead(indexFile);
        using var zip = new ZipArchive(file);

        foreach (var inp in zip.Entries)
        {
            if(inp.Name == "collection.info")
            {
                using var sr = new StreamReader(inp.Open());
                library.Description = await sr.ReadToEndAsync();
                continue;
            }

            if(inp.Name == "version.info")
            {
                using var sr = new StreamReader(inp.Open());
                library.Version = await sr.ReadToEndAsync();
                continue;
            }

            if (Path.GetExtension(inp.FullName) != ".inp")
            {
                continue;
            }

            using var inpReader = new InpReader(inp.Open());
            await foreach (var book in inpReader.ReadBooks())
            {
                book.ArchiveFile = Path.ChangeExtension(inp.Name, ".zip");
                book.Id = $"{inp.Name}_{book.LibId}";
                yield return book;
            }
        }
    }
}
