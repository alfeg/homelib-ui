namespace MyHomeLib.Web.Services.Search;

/// <param name="Q">Free text. Empty means "everything", ordered by the index default.</param>
/// <param name="Genres">Genre codes; multiple codes are OR-ed.</param>
/// <param name="Author">Exact author name, as produced by the indexer.</param>
/// <param name="Page">1-based.</param>
public record BookQuery(
    string? Q = null,
    IReadOnlyList<string>? Genres = null,
    string? Lang = null,
    int? YearFrom = null,
    int? YearTo = null,
    string? Author = null,
    string? Series = null,
    int Page = 1,
    int PageSize = 20,
    bool NewestFirst = false,
    bool SeriesOrder = false,
    bool GroupBySeries = false,
    bool FacetSeries = false,
    bool FacetEntities = false)
{
    public const int MaxPageSize = 100;

    public BookQuery Normalized() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize),
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(),
    };
}

public record BookSearchResult(
    IReadOnlyList<BookDocument> Books,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyDictionary<string, int> GenreCounts,
    IReadOnlyDictionary<string, int> LangCounts,
    IReadOnlyDictionary<string, int> SeriesCounts,
    IReadOnlyDictionary<string, int> AuthorCounts)
{
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <param name="FirstYear">Earliest year a book of this series entered the collection.</param>
/// <param name="LastYear">Latest such year. Equal to FirstYear for a single-year series.</param>
public record SeriesEntry(string Name, int Count, int? FirstYear = null, int? LastYear = null)
{
    /// <summary>"2016" or "2016–2019"; empty when the collection recorded no dates.</summary>
    public string YearRange =>
        FirstYear is null ? ""
        : LastYear is null || LastYear == FirstYear ? FirstYear.ToString()!
        : $"{FirstYear}–{LastYear}";
}

public record AuthorPage(string Author, BookSearchResult Books, IReadOnlyList<SeriesEntry> Series);

public record SearchWithEntities(
    BookSearchResult Books,
    IReadOnlyList<EntityHit> Authors,
    IReadOnlyList<EntityHit> Series);

/// <summary>
/// The single query path behind the JSON API, the OPDS feed and the thin HTML pages.
/// Keeping one implementation is what stops the three surfaces from drifting apart.
/// </summary>
public class LibrarySearchService(MeiliClient meili, IndexState state)
{
    private static readonly string[] Facets = ["genres", "lang"];

    /// <summary>Above this many books, an author's series year spans are not computed.</summary>
    private const int SeriesSummaryLimit = 1000;

    public async Task<BookSearchResult> SearchAsync(BookQuery query, CancellationToken ct = default)
    {
        query = query.Normalized();

        var request = new MeiliSearchRequest
        {
            // Folded to match the folded searchable fields: "мёд" and "мед" must find the
            // same books, and short words get no typo tolerance to paper over the difference.
            Q = RussianText.Fold(query.Q),
            Page = query.Page,
            HitsPerPage = query.PageSize,
            Filter = BuildFilter(query),
            // The collection has ~70k distinct series, so the series facet is requested only
            // where it is scoped to something small — an author's page, in practice.
            // Entity facets ride along with the query the user already ran: the author and
            // series names among the matching books are exactly the candidates for a card.
            Facets = query.FacetSeries ? [.. Facets, "series"]
                   : query.FacetEntities ? [.. Facets, "series", "authorList"]
                   : Facets,
            // Relevance ranking and explicit sorting are mutually exclusive in Meilisearch,
            // so sorting is only applied to the browse views, which carry no query text.
            Sort = query.GroupBySeries ? ["series:asc", "seriesIndex:asc", "title:asc"]
                 : query.SeriesOrder ? ["seriesIndex:asc", "title:asc"]
                 : query.NewestFirst ? ["year:desc", "id:desc"]
                 : null,
        };

        var response = await meili.SearchAsync(meili.IndexUid, request, ct);

        return new BookSearchResult(
            response.Hits,
            response.TotalHits,
            response.Page,
            response.HitsPerPage,
            response.TotalPages,
            response.FacetDistribution?.GetValueOrDefault("genres") ?? new Dictionary<string, int>(),
            response.FacetDistribution?.GetValueOrDefault("lang") ?? new Dictionary<string, int>(),
            response.FacetDistribution?.GetValueOrDefault("series") ?? new Dictionary<string, int>(),
            response.FacetDistribution?.GetValueOrDefault("authorList") ?? new Dictionary<string, int>());
    }

    /// <summary>
    /// A text search plus the author and series the query appears to name. Used by every
    /// surface that shows results to a person.
    /// </summary>
    public async Task<SearchWithEntities> SearchWithEntitiesAsync(BookQuery query, CancellationToken ct = default)
    {
        var wantsEntities = !string.IsNullOrWhiteSpace(query.Q);
        var result = await SearchAsync(query with { FacetEntities = wantsEntities }, ct);

        if (!wantsEntities) return new SearchWithEntities(result, [], []);

        return new SearchWithEntities(
            result,
            EntityMatcher.Match(query.Q, result.AuthorCounts),
            EntityMatcher.Match(query.Q, result.SeriesCounts));
    }

    public Task<BookDocument?> GetBookAsync(string uid, CancellationToken ct = default) =>
        meili.GetDocumentAsync(meili.IndexUid, uid, ct);

    /// <summary>Genre codes with counts across the whole collection.</summary>
    public async Task<IReadOnlyDictionary<string, int>> GenreCountsAsync(CancellationToken ct = default)
    {
        var response = await meili.SearchAsync(meili.IndexUid, new MeiliSearchRequest
        {
            Q = null,
            Page = 1,
            HitsPerPage = 1,
            Facets = ["genres"],
        }, ct);

        return response.FacetDistribution?.GetValueOrDefault("genres") ?? new Dictionary<string, int>();
    }

    public async Task<IReadOnlyDictionary<string, int>> LangCountsAsync(CancellationToken ct = default)
    {
        var response = await meili.SearchAsync(meili.IndexUid, new MeiliSearchRequest
        {
            Q = null,
            Page = 1,
            HitsPerPage = 1,
            Facets = ["lang"],
        }, ct);

        return response.FacetDistribution?.GetValueOrDefault("lang") ?? new Dictionary<string, int>();
    }

    /// <summary>
    /// Everything one author has: their series with book counts, plus the books that belong
    /// to no series. One search serves both — the series facet comes back with the page.
    /// </summary>
    public async Task<AuthorPage> AuthorAsync(string author, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await SearchAsync(new BookQuery(
            Author: author,
            Page: page,
            PageSize: pageSize,
            GroupBySeries: true,
            FacetSeries: true), ct);

        var series = await SummariseSeriesAsync(author, result, ct);
        return new AuthorPage(author, result, series);
    }

    /// <summary>
    /// Series of one author with counts and the span of years their books were added.
    /// </summary>
    /// <remarks>
    /// A facet gives counts but no year span, and one query per series would be an N+1. So
    /// the author's books are pulled once with only the three fields this needs. Above
    /// <see cref="SeriesSummaryLimit"/> books the pull is skipped and the facet counts stand
    /// on their own — prolific authors keep working, just without year ranges.
    /// </remarks>
    private async Task<IReadOnlyList<SeriesEntry>> SummariseSeriesAsync(
        string author, BookSearchResult result, CancellationToken ct)
    {
        var counts = result.SeriesCounts.Where(e => !string.IsNullOrWhiteSpace(e.Key)).ToList();
        if (counts.Count == 0) return [];

        Dictionary<string, (int? First, int? Last)> spans = [];

        if (result.Total <= SeriesSummaryLimit)
        {
            var all = await meili.SearchAsync(meili.IndexUid, new MeiliSearchRequest
            {
                Page = 1,
                HitsPerPage = SeriesSummaryLimit,
                Filter = [$"authorList = {Quote(author)}"],
                AttributesToRetrieve = ["series", "year"],
            }, ct);

            foreach (var book in all.Hits)
            {
                if (string.IsNullOrWhiteSpace(book.Series) || book.Year is not { } year) continue;

                var span = spans.GetValueOrDefault(book.Series);
                spans[book.Series] = (
                    span.First is null || year < span.First ? year : span.First,
                    span.Last is null || year > span.Last ? year : span.Last);
            }
        }

        return counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry =>
            {
                var span = spans.GetValueOrDefault(entry.Key);
                return new SeriesEntry(entry.Key, entry.Value, span.First, span.Last);
            })
            .ToList();
    }

    public IndexStatusSnapshot Status() => state.Snapshot();

    private static List<string>? BuildFilter(BookQuery query)
    {
        var filters = new List<string>();

        if (query.Genres is { Count: > 0 })
        {
            var codes = query.Genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => $"genres = {Quote(g)}");

            var joined = string.Join(" OR ", codes);
            if (!string.IsNullOrEmpty(joined)) filters.Add($"({joined})");
        }

        if (!string.IsNullOrWhiteSpace(query.Lang))
            filters.Add($"lang = {Quote(query.Lang)}");

        if (!string.IsNullOrWhiteSpace(query.Author))
            filters.Add($"authorList = {Quote(query.Author)}");

        if (!string.IsNullOrWhiteSpace(query.Series))
            filters.Add($"series = {Quote(query.Series)}");

        if (query.YearFrom is { } from) filters.Add($"year >= {from}");
        if (query.YearTo is { } to) filters.Add($"year <= {to}");

        return filters.Count > 0 ? filters : null;
    }

    /// <summary>Meilisearch filter literals are single-quoted; embedded quotes are escaped.</summary>
    private static string Quote(string value) => $"'{value.Replace("'", "\\'")}'";
}
