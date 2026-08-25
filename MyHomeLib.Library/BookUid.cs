namespace MyHomeLib.Library;

/// <summary>
/// Stable identifier for a book inside a collection.
/// </summary>
/// <remarks>
/// INPX ids are not unique: a real Flibusta collection carries ~57 duplicate ids across
/// 586k books, so keying storage on the raw id silently loses rows. The physical location
/// (archive + file + extension) is unique by construction and stable across reindexes,
/// which matters because these ids end up in OPDS acquisition links and download URLs.
/// FNV-1a/64 keeps the identifier to 16 hex chars, short enough for an e-ink URL bar.
/// </remarks>
public static class BookUid
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    public static string For(IBookItem book) =>
        For(book.ArchiveFile, book.File, book.Ext);

    public static string For(string? archiveFile, string? file, string? ext)
    {
        var hash = FnvOffsetBasis;
        hash = Absorb(hash, archiveFile);
        hash = Absorb(hash, "|");
        hash = Absorb(hash, file);
        hash = Absorb(hash, "|");
        hash = Absorb(hash, ext);
        return hash.ToString("x16");
    }

    private static ulong Absorb(ulong hash, string? value)
    {
        if (string.IsNullOrEmpty(value)) return hash;

        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= FnvPrime;
        }

        return hash;
    }
}
