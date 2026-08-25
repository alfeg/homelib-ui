namespace MyHomeLib.Library;

/// <summary>
/// Collection-level metadata and counters, filled in while an INPX is being read.
/// </summary>
public class InpxLibrary
{
    /// <summary>Contents of <c>collection.info</c>, when the archive carries one.</summary>
    public string? Description { get; set; }

    /// <summary>Contents of <c>version.info</c>, when the archive carries one.</summary>
    public string? Version { get; set; }

    /// <summary>Rows seen, including deleted ones.</summary>
    public long BooksRead { get; set; }

    /// <summary>Rows actually yielded to the caller.</summary>
    public long BooksAdded { get; set; }
}
