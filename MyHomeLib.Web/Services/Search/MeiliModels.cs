using System.Text.Json.Serialization;

namespace MyHomeLib.Web.Services.Search;

// ── Wire models for the Meilisearch HTTP API ────────────────────────────────
// Hand-rolled rather than taken from the official SDK: this branch targets low-memory
// devices and a small VPS, and the surface actually used here is six endpoints.

public class MeiliTask
{
    [JsonPropertyName("taskUid")] public int TaskUid { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("error")] public MeiliTaskError? Error { get; set; }
}

public class MeiliTaskError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
}

public class MeiliIndexSettings
{
    [JsonPropertyName("searchableAttributes")] public string[]? SearchableAttributes { get; set; }
    [JsonPropertyName("filterableAttributes")] public string[]? FilterableAttributes { get; set; }
    [JsonPropertyName("sortableAttributes")] public string[]? SortableAttributes { get; set; }
    [JsonPropertyName("displayedAttributes")] public string[]? DisplayedAttributes { get; set; }
    [JsonPropertyName("faceting")] public MeiliFaceting? Faceting { get; set; }
    [JsonPropertyName("pagination")] public MeiliPaginationSettings? Pagination { get; set; }
}

public class MeiliFaceting
{
    [JsonPropertyName("maxValuesPerFacet")] public int MaxValuesPerFacet { get; set; }

    /// <summary>
    /// Meilisearch defaults to ordering facet values alphabetically and then truncating at
    /// maxValuesPerFacet — so a capped facet silently returns the alphabetical head, not the
    /// biggest values. Searching "толстой" returned 400 authors beginning with "А" and no
    /// Лев Толстой at all. Ordering by count makes the cap keep what actually matters.
    /// </summary>
    [JsonPropertyName("sortFacetValuesBy")] public Dictionary<string, string>? SortFacetValuesBy { get; set; }
}

public class MeiliPaginationSettings
{
    /// <summary>Meilisearch caps reachable hits at 1000 by default; OPDS paging needs more.</summary>
    [JsonPropertyName("maxTotalHits")] public int MaxTotalHits { get; set; }
}

public class MeiliIndexStats
{
    [JsonPropertyName("numberOfDocuments")] public long NumberOfDocuments { get; set; }
    [JsonPropertyName("isIndexing")] public bool IsIndexing { get; set; }
}

public class MeiliSearchRequest
{
    [JsonPropertyName("q")] public string? Q { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; } = 1;
    [JsonPropertyName("hitsPerPage")] public int HitsPerPage { get; set; } = 20;
    [JsonPropertyName("filter")] public List<string>? Filter { get; set; }
    [JsonPropertyName("facets")] public string[]? Facets { get; set; }
    [JsonPropertyName("sort")] public string[]? Sort { get; set; }
    [JsonPropertyName("attributesToRetrieve")] public string[]? AttributesToRetrieve { get; set; }

    /// <summary>
    /// "all" requires every query word to match. Meilisearch defaults to "last", which
    /// drops trailing words until something matches — so "tolstoy war" quietly degrades to
    /// a fuzzy match on "tolstoy" alone. The SPA's MiniSearch index was strict AND, and
    /// users of a 586k-book library need the narrowing, not the widening.
    /// </summary>
    [JsonPropertyName("matchingStrategy")] public string MatchingStrategy { get; set; } = "all";
}

public class MeiliSearchResponse
{
    [JsonPropertyName("hits")] public List<BookDocument> Hits { get; set; } = [];
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("hitsPerPage")] public int HitsPerPage { get; set; }
    [JsonPropertyName("totalHits")] public int TotalHits { get; set; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
    [JsonPropertyName("facetDistribution")] public Dictionary<string, Dictionary<string, int>>? FacetDistribution { get; set; }
}

/// <summary>
/// Single-document record describing what is currently indexed. Lets a restarted
/// container skip a multi-minute reindex when the collection has not changed.
/// </summary>
public class IndexStateDocument
{
    [JsonPropertyName("uid")] public string Uid { get; set; } = "state";
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("collectionTitle")] public string? CollectionTitle { get; set; }
    [JsonPropertyName("collectionVersion")] public string? CollectionVersion { get; set; }
    [JsonPropertyName("indexedAt")] public string IndexedAt { get; set; } = "";

    /// <summary>
    /// Fingerprint per book archive. These collections are append-only in practice, so a
    /// new INPX usually differs in a handful of archives; keeping them individually lets
    /// startup re-index just those instead of rebuilding 586k documents.
    /// </summary>
    [JsonPropertyName("archives")] public Dictionary<string, string> Archives { get; set; } = [];

    /// <summary>
    /// Shape of the indexed documents. Content fingerprints cannot see a schema change —
    /// adding a field leaves every archive's hash identical, so the incremental diff finds
    /// nothing to do and the new field silently never lands. Bumping this forces a rebuild.
    /// </summary>
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
}
