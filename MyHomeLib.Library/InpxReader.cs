using System.IO.Compression;

namespace MyHomeLib.Library;

/// <summary>
/// Reads an <c>.inpx</c> archive: a ZIP containing one <c>.inp</c> entry per book archive,
/// plus optional <c>collection.info</c> / <c>version.info</c> metadata entries.
/// </summary>
public sealed class InpxReader
{
    /// <summary>
    /// Streams every book in the collection. The books of one <c>.inp</c> entry all live in the
    /// ZIP named after that entry (<c>fb2-000024-030559.inp</c> → <c>fb2-000024-030559.zip</c>).
    /// </summary>
    /// <param name="inpxStream">Seekable stream over the .inpx file.</param>
    /// <param name="library">Populated with collection metadata and running counters.</param>
    /// <param name="includeDeleted">When false (default) rows flagged deleted are skipped.</param>
    public static IEnumerable<BookItem> ReadLibrary(
        Stream inpxStream,
        InpxLibrary library,
        bool includeDeleted = false,
        IProgress<InpxProgress>? progress = null)
    {
        using var zip = new ZipArchive(inpxStream, ZipArchiveMode.Read, leaveOpen: true);

        // Book count is unknown until the whole archive is read, so progress is reported
        // per .inp entry — there are a couple of hundred, which is granular enough.
        var totalEntries = zip.Entries.Count(e => e.FullName.EndsWith(".inp", StringComparison.OrdinalIgnoreCase));
        var entriesDone = 0;

        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Equals("collection.info", StringComparison.OrdinalIgnoreCase))
            {
                library.Description = ReadEntryText(entry);
                continue;
            }

            if (entry.Name.Equals("version.info", StringComparison.OrdinalIgnoreCase))
            {
                library.Version = ReadEntryText(entry);
                continue;
            }

            if (!entry.FullName.EndsWith(".inp", StringComparison.OrdinalIgnoreCase))
                continue;

            var archiveFile = ToArchiveFile(entry.Name);
            var bytes = ReadEntryBytes(entry);

            foreach (var book in InpReader.ReadBooks(bytes, archiveFile))
            {
                library.BooksRead++;

                if (book.Deleted && !includeDeleted) continue;

                library.BooksAdded++;
                yield return book;
            }

            entriesDone++;
            progress?.Report(new InpxProgress(entriesDone, totalEntries, library.BooksAdded));
        }
    }

    /// <summary>
    /// Reads only <c>collection.info</c> / <c>version.info</c>, without parsing any books.
    /// Cheap enough to run before deciding whether a reindex is needed.
    /// </summary>
    public static InpxLibrary ReadMetadata(Stream inpxStream)
    {
        var library = new InpxLibrary();
        using var zip = new ZipArchive(inpxStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Equals("collection.info", StringComparison.OrdinalIgnoreCase))
                library.Description = ReadEntryText(entry);
            else if (entry.Name.Equals("version.info", StringComparison.OrdinalIgnoreCase))
                library.Version = ReadEntryText(entry);
        }

        return library;
    }

    /// <summary><c>fb2-000024-030559.inp</c> → <c>fb2-000024-030559.zip</c>.</summary>
    public static string ToArchiveFile(string entryName)
    {
        var fileName = entryName.Split('/').Last();
        return Path.ChangeExtension(fileName, ".zip");
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream(capacity: (int)Math.Min(entry.Length, int.MaxValue));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadEntryText(ZipArchiveEntry entry) =>
        TextDecoding.Decode(ReadEntryBytes(entry)).Trim();
}
