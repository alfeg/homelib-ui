using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Opds;
using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web.Controllers;

/// <summary>
/// OPDS 1.2 catalog. This is the path an e-reader takes: PocketBook's built-in
/// Library → Network client browses these feeds and downloads straight into the device.
/// </summary>
[ApiController]
[Route("opds")]
public class OpdsController(
    LibrarySearchService search,
    GenreLabelService genreLabels,
    IndexState state,
    IOptions<LibraryConfig> libraryConfig) : ControllerBase
{
    private const int PageSize = 25;

    private readonly LibraryConfig _config = libraryConfig.Value;

    private string CollectionTitle => state.CollectionTitle ?? _config.Title;

    /// <summary>Genres kept out of the catalog — see <see cref="LibraryConfig.BlockedGenres"/>.</summary>
    private string[] Blocked => _config.BlockedGenres;

    // ── Root ─────────────────────────────────────────────────────────────────

    [HttpGet("")]
    [HttpGet("index.xml")]
    public IActionResult Root()
    {
        if (!state.IsReady) return NotReadyFeed("/opds");

        var entries = new List<OpdsEntry>
        {
            Navigation("new", "Recently added", $"Newest arrivals in {CollectionTitle}", "/opds/new"),
            Navigation("genres", "By genre", "Browse the collection by genre", "/opds/genres"),
            Navigation("langs", "By language", "Browse the collection by language", "/opds/langs"),
            Navigation("all", "All books", $"{state.TotalBooks:N0} books", "/opds/all"),
        };

        return Feed(new OpdsFeed
        {
            Id = "urn:homelib:feed:root",
            Title = CollectionTitle,
            Subtitle = $"{state.TotalBooks:N0} books",
            SelfHref = "/opds",
            IsAcquisition = false,
            Updated = state.IndexedAt ?? DateTimeOffset.UtcNow,
            Entries = entries,
        });
    }

    /// <summary>
    /// OpenSearch descriptor. Without this the reader's own search box has nothing to call.
    /// </summary>
    [HttpGet("opensearch.xml")]
    public ContentResult OpenSearch()
    {
        // Kept out of the interpolated literal: {searchTerms} is an OpenSearch placeholder,
        // not a C# hole, and braces inside a raw interpolated string get messy fast.
        const string searchTemplate = "/opds/search?q={searchTerms}";

        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/">
              <ShortName>{System.Security.SecurityElement.Escape(CollectionTitle)}</ShortName>
              <Description>Search the {System.Security.SecurityElement.Escape(CollectionTitle)} collection</Description>
              <InputEncoding>UTF-8</InputEncoding>
              <OutputEncoding>UTF-8</OutputEncoding>
              <Url type="{OpdsFeed.AcquisitionType}" template="{searchTemplate}"/>
            </OpenSearchDescription>
            """;

        return Content(xml, "application/opensearchdescription+xml", Encoding.UTF8);
    }

    // ── Acquisition feeds ────────────────────────────────────────────────────

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/search");

        var found = await search.SearchWithEntitiesAsync(
            new BookQuery(Q: q, ExcludedGenres: Blocked, Page: page, PageSize: PageSize), ct);

        // Matched authors and series lead the first page as sub-catalogs, so a reader can
        // step into the shelf rather than scrolling every loose title.
        var leading = new List<OpdsEntry>();
        if (found.Books.Page == 1)
        {
            leading.AddRange(found.Authors.Select(hit => Navigation(
                $"author:{hit.Name}", hit.Name, $"{hit.Count:N0} books",
                $"/opds/author?name={Uri.EscapeDataString(hit.Name)}", OpdsFeed.AcquisitionType)));

            leading.AddRange(found.Series.Select(hit => Navigation(
                $"series:{hit.Name}", hit.Name, $"{hit.Count:N0} books",
                $"/opds/series?name={Uri.EscapeDataString(hit.Name)}", OpdsFeed.AcquisitionType)));
        }

        return BookFeed(
            id: $"urn:homelib:feed:search:{q}",
            title: string.IsNullOrWhiteSpace(q) ? "Search" : $"Search: {q}",
            basePath: $"/opds/search?q={Uri.EscapeDataString(q ?? "")}",
            result: found.Books,
            leadingEntries: leading);
    }

    [HttpGet("all")]
    public async Task<IActionResult> All([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/all");

        var result = await search.SearchAsync(new BookQuery(ExcludedGenres: Blocked, Page: page, PageSize: PageSize), ct);
        return BookFeed("urn:homelib:feed:all", "All books", "/opds/all", result);
    }

    [HttpGet("new")]
    public async Task<IActionResult> New([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/new");

        var result = await search.SearchAsync(
            new BookQuery(ExcludedGenres: Blocked, Page: page, PageSize: PageSize, NewestFirst: true), ct);

        return BookFeed("urn:homelib:feed:new", "Recently added", "/opds/new", result);
    }

    [HttpGet("genre/{code}")]
    public async Task<IActionResult> Genre(string code, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed($"/opds/genre/{code}");
        if (Blocked.Contains(code, StringComparer.OrdinalIgnoreCase)) return NotFound();

        var result = await search.SearchAsync(
            new BookQuery(Genres: [code], ExcludedGenres: Blocked, Page: page, PageSize: PageSize), ct);

        return BookFeed(
            $"urn:homelib:feed:genre:{code}",
            genreLabels.Label(code),
            $"/opds/genre/{Uri.EscapeDataString(code)}",
            result,
            upHref: "/opds/genres");
    }

    [HttpGet("lang/{code}")]
    public async Task<IActionResult> Language(string code, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed($"/opds/lang/{code}");

        var result = await search.SearchAsync(
            new BookQuery(Lang: code, ExcludedGenres: Blocked, Page: page, PageSize: PageSize), ct);

        return BookFeed(
            $"urn:homelib:feed:lang:{code}",
            code,
            $"/opds/lang/{Uri.EscapeDataString(code)}",
            result,
            upHref: "/opds/langs");
    }

    /// <summary>An author's books, in series order, with their series as sub-catalogs.</summary>
    [HttpGet("author")]
    public async Task<IActionResult> Author([FromQuery] string name, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/author");
        if (string.IsNullOrWhiteSpace(name)) return NotFound();

        var author = await search.AuthorAsync(name, page, PageSize, ct, Blocked);
        var basePath = $"/opds/author?name={Uri.EscapeDataString(name)}";

        // Series appear as navigation entries ahead of the books, so a reader can jump
        // straight into a cycle instead of paging through everything the author wrote.
        var entries = new List<OpdsEntry>();
        if (page == 1)
        {
            entries.AddRange(author.Series.Select(entry => Navigation(
                $"series:{entry.Name}",
                entry.Name,
                $"{entry.Count:N0} books",
                $"/opds/series?name={Uri.EscapeDataString(entry.Name)}&author={Uri.EscapeDataString(name)}",
                OpdsFeed.AcquisitionType)));
        }

        entries.AddRange(author.Books.Books.Select(ToEntry));

        return Feed(new OpdsFeed
        {
            Id = $"urn:homelib:feed:author:{name}",
            Title = name,
            Subtitle = $"{author.Books.Total:N0} books",
            SelfHref = Paged(basePath, "&", author.Books.Page),
            UpHref = "/opds",
            IsAcquisition = true,
            NextHref = author.Books.HasNextPage ? Paged(basePath, "&", author.Books.Page + 1) : null,
            PreviousHref = author.Books.HasPreviousPage ? Paged(basePath, "&", author.Books.Page - 1) : null,
            Entries = entries,
        });
    }

    /// <summary>One series in reading order.</summary>
    [HttpGet("series")]
    public async Task<IActionResult> Series(
        [FromQuery] string name,
        [FromQuery] string? author,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/series");
        if (string.IsNullOrWhiteSpace(name)) return NotFound();

        var result = await search.SearchAsync(new BookQuery(
            Series: name,
            Author: string.IsNullOrWhiteSpace(author) ? null : author,
            Page: page,
            PageSize: PageSize,
            SeriesOrder: true), ct);

        var basePath = $"/opds/series?name={Uri.EscapeDataString(name)}"
                     + (string.IsNullOrWhiteSpace(author) ? "" : $"&author={Uri.EscapeDataString(author)}");

        return BookFeed($"urn:homelib:feed:series:{name}", name, basePath, result,
            upHref: string.IsNullOrWhiteSpace(author) ? "/opds" : $"/opds/author?name={Uri.EscapeDataString(author)}");
    }

    // ── Navigation feeds ─────────────────────────────────────────────────────

    /// <summary>
    /// Two levels: categories first, then the genres inside one. A flat list of ~278 genre
    /// entries is a punishing scroll on an e-ink page-turn device.
    /// </summary>
    [HttpGet("genres")]
    public async Task<IActionResult> Genres([FromQuery] string? category, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/genres");

        var counts = await search.GenreCountsAsync(ct, Blocked);
        var categories = genreLabels.Categorise(counts);

        if (string.IsNullOrWhiteSpace(category))
        {
            var entries = categories
                .Select(c => Navigation(
                    $"genre-category:{c.Name}",
                    c.Name,
                    $"{c.Count:N0} books",
                    $"/opds/genres?category={Uri.EscapeDataString(c.Name)}"))
                .ToList();

            return Feed(new OpdsFeed
            {
                Id = "urn:homelib:feed:genres",
                Title = "By genre",
                SelfHref = "/opds/genres",
                UpHref = "/opds",
                IsAcquisition = false,
                Entries = entries,
            });
        }

        var picked = categories.FirstOrDefault(c => string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase));
        if (picked is null) return NotFound();

        var genreEntries = picked.Genres
            .Select(g => Navigation(
                $"genre:{g.Code}",
                g.Label,
                $"{g.Count:N0} books",
                $"/opds/genre/{Uri.EscapeDataString(g.Code)}",
                OpdsFeed.AcquisitionType))
            .ToList();

        return Feed(new OpdsFeed
        {
            Id = $"urn:homelib:feed:genres:{picked.Name}",
            Title = picked.Name,
            SelfHref = $"/opds/genres?category={Uri.EscapeDataString(picked.Name)}",
            UpHref = "/opds/genres",
            IsAcquisition = false,
            Entries = genreEntries,
        });
    }

    [HttpGet("langs")]
    public async Task<IActionResult> Languages(CancellationToken ct)
    {
        if (!state.IsReady) return NotReadyFeed("/opds/langs");

        var counts = await search.LangCountsAsync(ct, Blocked);

        var entries = counts
            .Where(c => c.Value > 0 && !string.IsNullOrWhiteSpace(c.Key))
            .OrderByDescending(c => c.Value)
            .Select(c => Navigation(
                $"lang:{c.Key}",
                c.Key,
                $"{c.Value:N0} books",
                $"/opds/lang/{Uri.EscapeDataString(c.Key)}",
                OpdsFeed.AcquisitionType))
            .ToList();

        return Feed(new OpdsFeed
        {
            Id = "urn:homelib:feed:langs",
            Title = "By language",
            SelfHref = "/opds/langs",
            UpHref = "/opds",
            IsAcquisition = false,
            Entries = entries,
        });
    }

    // ── Building blocks ──────────────────────────────────────────────────────

    private IActionResult BookFeed(
        string id, string title, string basePath, BookSearchResult result,
        string? upHref = "/opds", IReadOnlyList<OpdsEntry>? leadingEntries = null)
    {
        var entries = (leadingEntries ?? []).Concat(result.Books.Select(ToEntry)).ToList();
        var separator = basePath.Contains('?') ? "&" : "?";

        return Feed(new OpdsFeed
        {
            Id = id,
            Title = title,
            Subtitle = $"{result.Total:N0} books",
            SelfHref = Paged(basePath, separator, result.Page),
            UpHref = upHref,
            IsAcquisition = true,
            NextHref = result.HasNextPage ? Paged(basePath, separator, result.Page + 1) : null,
            PreviousHref = result.HasPreviousPage ? Paged(basePath, separator, result.Page - 1) : null,
            Entries = entries,
        });
    }

    private static string Paged(string basePath, string separator, int page) =>
        page <= 1 ? basePath : $"{basePath}{separator}page={page}";

    private OpdsEntry ToEntry(BookDocument book)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(book.Series)) details.Add(book.SeriesDisplay());
        if (book.Size > 0) details.Add($"{book.Size / 1024:N0} KB");
        if (!string.IsNullOrWhiteSpace(book.Lang)) details.Add(book.Lang);

        return new OpdsEntry(
            Id: $"urn:homelib:book:{book.Uid}",
            Title: string.IsNullOrWhiteSpace(book.Title) ? book.File : book.Title,
            Content: details.Count > 0 ? string.Join(" · ", details) : null,
            Links:
            [
                // Zipped first: several e-reader clients pick the first acquisition link they
                // understand, and fb2+zip is the more widely supported of the two.
                new OpdsLink("http://opds-spec.org/acquisition", BookDownloadService.Fb2ZipContentType, $"/download/{book.Uid}.zip"),
                new OpdsLink("http://opds-spec.org/acquisition", BookDownloadService.Fb2ContentType, $"/download/{book.Uid}"),
            ])
        {
            Authors = book.AuthorList,
            Language = book.Lang,
            Categories = [.. book.Genres.Select(g => (g, genreLabels.Label(g)))],
        };
    }

    private static OpdsEntry Navigation(string key, string title, string? content, string href, string? type = null) =>
        new(
            Id: $"urn:homelib:nav:{key}",
            Title: title,
            Content: content,
            Links: [new OpdsLink("subsection", type ?? OpdsFeed.NavigationType, href)]);

    private ContentResult Feed(OpdsFeed feed)
    {
        var contentType = feed.IsAcquisition ? OpdsFeed.AcquisitionType : OpdsFeed.NavigationType;
        return Content(feed.ToXml(), contentType, Encoding.UTF8);
    }

    /// <summary>
    /// A device that asks for the catalog during the first-boot index build gets a valid
    /// feed saying so, rather than a 503 it would render as a connection failure.
    /// </summary>
    private IActionResult NotReadyFeed(string selfHref)
    {
        var snapshot = state.Snapshot();

        var entry = new OpdsEntry(
            Id: "urn:homelib:nav:indexing",
            Title: snapshot.Error is null
                ? $"Building the index — {snapshot.Percent}%"
                : "Indexing failed",
            Content: snapshot.Error ?? $"{snapshot.Message}. Refresh in a minute.",
            Links: [new OpdsLink("subsection", OpdsFeed.NavigationType, "/opds")]);

        return Feed(new OpdsFeed
        {
            Id = "urn:homelib:feed:not-ready",
            Title = CollectionTitle,
            Subtitle = snapshot.Message,
            SelfHref = selfHref,
            IsAcquisition = false,
            Entries = [entry],
        });
    }
}
