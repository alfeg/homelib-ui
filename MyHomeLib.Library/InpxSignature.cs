namespace MyHomeLib.Library;

/// <param name="Value">Stable content fingerprint of the whole collection.</param>
/// <param name="BookCount">Books counted while fingerprinting.</param>
/// <param name="Archives">Per-archive fingerprint, keyed by the ZIP the books live in.</param>
public readonly record struct InpxSignatureResult(
    string Value,
    long BookCount,
    IReadOnlyDictionary<string, string> Archives);

/// <summary>
/// Fingerprints an INPX by what it <em>contains</em>, not by how it arrived — as a whole and
/// per book archive.
/// </summary>
/// <remarks>
/// The obvious signature — version.info plus the archive's byte length — is unstable in
/// exactly the situations that matter: the same collection fetched through a different
/// magnet, re-zipped by a different tool, or read from a local file instead of the torrent
/// all produce different byte lengths and trigger a needless multi-minute reindex.
///
/// Per-archive fingerprints exist because these collections are effectively append-only: a
/// new magnet almost always means "the same books plus some new ones", arriving as new .inp
/// entries. Comparing per archive turns that from a full rebuild into re-indexing the two or
/// three archives that actually changed.
///
/// Per-book hashes are summed rather than chained, which makes each result independent of
/// the order entries happen to appear in the ZIP — another thing that varies between
/// rebuilds of the same collection.
/// </remarks>
public static class InpxSignature
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    public static InpxSignatureResult Compute(Stream inpxStream, bool includeDeleted = false)
    {
        var library = new InpxLibrary();
        var perArchive = new Dictionary<string, (ulong Hash, long Count)>();
        ulong total = 0;
        long count = 0;

        foreach (var book in InpxReader.ReadLibrary(inpxStream, library, includeDeleted))
        {
            var hash = HashBook(book);
            var archive = book.ArchiveFile ?? "";

            var current = perArchive.GetValueOrDefault(archive);
            perArchive[archive] = (unchecked(current.Hash + hash), current.Count + 1);

            total = unchecked(total + hash);
            count++;
        }

        var archives = perArchive.ToDictionary(
            entry => entry.Key,
            entry => $"{entry.Value.Count}:{entry.Value.Hash:x16}");

        return new InpxSignatureResult($"v5:{count}:{total:x16}", count, archives);
    }

    private static ulong HashBook(BookItem book)
    {
        var hash = FnvOffsetBasis;
        hash = Absorb(hash, BookUid.For(book));
        hash = Absorb(hash, book.Title);
        hash = Absorb(hash, book.Authors);
        hash = Absorb(hash, book.Series);
        hash = Absorb(hash, book.Genre);
        hash = Absorb(hash, book.Lang);
        return hash;
    }

    private static ulong Absorb(ulong hash, string? value)
    {
        if (string.IsNullOrEmpty(value)) return hash;

        foreach (var ch in value)
        {
            hash ^= ch;
            hash = unchecked(hash * FnvPrime);
        }

        return hash;
    }
}
