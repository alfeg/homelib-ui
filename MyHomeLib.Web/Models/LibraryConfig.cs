namespace MyHomeLib.Web.Models;

/// <summary>
/// Which library this instance serves. Operator-controlled only — the magnet URI is never
/// accepted from a request on this branch, which is what keeps the instance from being
/// turned into an open torrent relay.
/// </summary>
public class LibraryConfig
{
    /// <summary>Magnet URI of the library torrent. Required unless <see cref="InpxPath"/> is set.</summary>
    public string? MagnetUri { get; set; }

    /// <summary>
    /// Optional path to a local .inpx file. When set, indexing reads it directly and
    /// TorrServe is not contacted at startup. Intended for development.
    /// </summary>
    public string? InpxPath { get; set; }

    /// <summary>Human-readable collection name, shown in OPDS and the thin UI.</summary>
    public string Title { get; set; } = "MyHomeLib";

    /// <summary>Re-index on startup even when the stored signature matches.</summary>
    public bool ForceReindex { get; set; }

    /// <summary>Documents pushed to Meilisearch per request.</summary>
    public int IndexBatchSize { get; set; } = 10_000;
}
