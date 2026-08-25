namespace MyHomeLib.Library;

/// <param name="EntriesDone">.inp entries parsed so far.</param>
/// <param name="TotalEntries">.inp entries in the archive.</param>
/// <param name="BooksRead">Books yielded so far.</param>
public readonly record struct InpxProgress(int EntriesDone, int TotalEntries, long BooksRead)
{
    public int Percent => TotalEntries <= 0 ? 0 : (int)(EntriesDone * 100L / TotalEntries);
}
