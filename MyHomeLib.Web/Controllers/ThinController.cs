using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services.Search;
using MyHomeLib.Web.Services.Thin;

namespace MyHomeLib.Web.Controllers;

/// <summary>
/// Server-rendered pages for low-power devices — the reader's own browser, an old phone,
/// anything that cannot afford a SPA. No JavaScript is served by any route here.
/// </summary>
[Route("m")]
public class ThinController(
    LibrarySearchService search,
    GenreLabelService genreLabels,
    IndexState state,
    IOptions<LibraryConfig> libraryConfig) : ControllerBase
{
    private const int PageSize = 20;

    /// <summary>How often the indexing page reloads itself while the library is building.</summary>
    private const int RefreshSeconds = 5;

    private readonly LibraryConfig _config = libraryConfig.Value;

    private string CollectionTitle => state.CollectionTitle ?? _config.Title;

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!state.IsReady) return NotReadyPage();

        var page = new ThinHtml(CollectionTitle)
            .Header(CollectionTitle, $"{state.TotalBooks:N0} books")
            .SearchForm("/m/search", null)
            .Caption("Browse")
            .List(
            [
                ThinHtml.Row("/m/genres", "By genre", null, "›"),
                ThinHtml.Row("/m/new", "Recently added", null, "›"),
                ThinHtml.Row("/m/all", "All books", $"{state.TotalBooks:N0} titles", "›"),
            ])
            .Note("OPDS catalog at /opds");

        return Html(page);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? genre,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();

        var found = await search.SearchWithEntitiesAsync(new BookQuery(
            Q: q,
            Genres: string.IsNullOrWhiteSpace(genre) ? null : [genre],
            Page: page,
            PageSize: PageSize), ct);

        var heading = string.IsNullOrWhiteSpace(genre)
            ? (string.IsNullOrWhiteSpace(q) ? "All books" : $"“{q}”")
            : genreLabels.Label(genre);

        var query = BuildQuery(("q", q), ("genre", genre));

        return Html(Results(heading, found.Books, query, q, genre, entities: found));
    }

    [HttpGet("all")]
    public Task<IActionResult> All([FromQuery] int page = 1, CancellationToken ct = default) =>
        Search(null, null, page, ct);

    [HttpGet("new")]
    public async Task<IActionResult> New([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();

        var result = await search.SearchAsync(
            new BookQuery(Page: page, PageSize: PageSize, NewestFirst: true), ct);

        return Html(Results("Recently added", result, "", null, null, basePath: "/m/new"));
    }

    [HttpGet("genres")]
    public async Task<IActionResult> Genres([FromQuery] string? category, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();

        var categories = genreLabels.Categorise(await search.GenreCountsAsync(ct));

        if (string.IsNullOrWhiteSpace(category))
        {
            var items = categories.Select(c => ThinHtml.Row(
                $"/m/genres?category={Uri.EscapeDataString(c.Name)}", c.Name, $"{c.Count:N0} books", "›"));

            return Html(new ThinHtml($"Genres — {CollectionTitle}")
                .Header("By genre", null, ("/m", "Home"))
                .List(items));
        }

        var picked = categories.FirstOrDefault(c => string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase));
        if (picked is null) return NotFound();

        var genreItems = picked.Genres.Select(g => ThinHtml.Row(
            $"/m/search?genre={Uri.EscapeDataString(g.Code)}", g.Label, $"{g.Count:N0} books", "›"));

        return Html(new ThinHtml($"{picked.Name} — {CollectionTitle}")
            .Header(picked.Name, $"{picked.Count:N0} books", ("/m", "Home"), ("/m/genres", "Genres"))
            .List(genreItems));
    }

    [HttpGet("book/{uid}")]
    public async Task<IActionResult> Book(string uid, CancellationToken ct)
    {
        if (!state.IsReady) return NotReadyPage();

        var book = await search.GetBookAsync(uid, ct);
        if (book is null) return NotFound();

        var page = new ThinHtml($"{book.Title} — {CollectionTitle}")
            .Header(
                string.IsNullOrWhiteSpace(book.Title) ? book.File : book.Title,
                string.IsNullOrWhiteSpace(book.Authors) ? null : book.Authors,
                ("/m", "Home"), ("/m/genres", "Genres"));

        if (!string.IsNullOrWhiteSpace(book.Series))
        {
            page.Caption("Series");
            page.List([ThinHtml.Row(
                $"/m/series?name={Uri.EscapeDataString(book.Series)}",
                book.SeriesDisplay(), null, "›")]);
        }

        if (book.AuthorList.Length > 0)
        {
            page.Caption(book.AuthorList.Length == 1 ? "Author" : "Authors");
            page.List(book.AuthorList.Select(a => ThinHtml.Row(
                $"/m/author?name={Uri.EscapeDataString(a)}", a, null, "›")));
        }

        if (book.Genres.Length > 0)
        {
            page.Caption(book.Genres.Length == 1 ? "Genre" : "Genres");
            page.List(book.Genres.Select(g => ThinHtml.Row(
                $"/m/search?genre={Uri.EscapeDataString(g)}",
                genreLabels.Label(g),
                genreLabels.Category(g) is { Length: > 0 } category ? category : null,
                "›")));
        }

        page.Caption("Details");
        page.Facts(
        [
            ("Format", $".{book.Ext}"),
            ("Size", book.Size > 0 ? $"{book.Size / 1024:N0} KB" : "—"),
            ("Language", string.IsNullOrWhiteSpace(book.Lang) ? "—" : book.Lang),
            // Labelled "added" deliberately: the INPX date records when the book entered the
            // collection, not when it was written or published.
            ("Added to library", book.Added ?? (book.Year?.ToString() ?? "—")),
            ("Library id", book.Id > 0 ? book.Id.ToString() : "—"),
        ]);

        page.Caption("Download");
        page.Raw(ThinHtml.Link($"/download/{book.Uid}", $".{book.Ext}", "d"));
        page.Raw(ThinHtml.Link($"/download/{book.Uid}.zip", $".{book.Ext}.zip", "d"));

        return Html(page);
    }

    /// <summary>
    /// Everything by one author: their series first, then the books themselves. The series
    /// list comes from the same query as the books, so this costs one search, not two.
    /// </summary>
    [HttpGet("author")]
    public async Task<IActionResult> Author([FromQuery] string name, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();
        if (string.IsNullOrWhiteSpace(name)) return NotFound();

        var author = await search.AuthorAsync(name, page, PageSize, ct);

        var html = new ThinHtml($"{name} — {CollectionTitle}")
            .Header(name, $"{author.Books.Total:N0} books", ("/m", "Home"));

        if (author.Series.Count > 0)
        {
            html.Caption($"Series ({author.Series.Count})");
            html.List(author.Series.Select(entry => ThinHtml.Row(
                $"/m/series?name={Uri.EscapeDataString(entry.Name)}&author={Uri.EscapeDataString(name)}",
                entry.Name,
                entry.YearRange.Length > 0
                    ? $"{entry.Count:N0} books · added {entry.YearRange}"
                    : $"{entry.Count:N0} books",
                "›")));
        }

        html.Caption("Books");
        html.List(author.Books.Books.Select(BookRow));

        var query = BuildQuery(("name", name));
        html.Pager(
            author.Books.HasPreviousPage ? $"/m/author{query}&page={author.Books.Page - 1}" : null,
            author.Books.HasNextPage ? $"/m/author{query}&page={author.Books.Page + 1}" : null,
            $"{author.Books.Page} / {author.Books.TotalPages:N0}");

        return Html(html);
    }

    /// <summary>One series, in reading order rather than relevance order.</summary>
    [HttpGet("series")]
    public async Task<IActionResult> Series(
        [FromQuery] string name,
        [FromQuery] string? author,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();
        if (string.IsNullOrWhiteSpace(name)) return NotFound();

        var result = await search.SearchAsync(new BookQuery(
            Series: name,
            Author: string.IsNullOrWhiteSpace(author) ? null : author,
            Page: page,
            PageSize: PageSize,
            SeriesOrder: true), ct);

        var crumbs = string.IsNullOrWhiteSpace(author)
            ? new[] { ("/m", "Home") }
            : [("/m", "Home"), ($"/m/author?name={Uri.EscapeDataString(author)}", author)];

        var html = new ThinHtml($"{name} — {CollectionTitle}")
            .Header(name, $"{result.Total:N0} books", crumbs)
            .List(result.Books.Select(book => ThinHtml.Row(
                $"/m/book/{book.Uid}",
                book.SeriesIndex is { } index ? $"{index}. {book.Title}" : book.Title,
                string.IsNullOrWhiteSpace(book.Authors) ? null : book.Authors)));

        var query = BuildQuery(("name", name), ("author", author));
        html.Pager(
            result.HasPreviousPage ? $"/m/series{query}&page={result.Page - 1}" : null,
            result.HasNextPage ? $"/m/series{query}&page={result.Page + 1}" : null,
            $"{result.Page} / {result.TotalPages:N0}");

        return Html(html);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private static string BookRow(BookDocument book)
    {
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(book.Authors)) meta.Add(book.Authors);
        if (!string.IsNullOrWhiteSpace(book.Series)) meta.Add(book.SeriesDisplay());

        return ThinHtml.Row(
            $"/m/book/{book.Uid}",
            string.IsNullOrWhiteSpace(book.Title) ? book.File : book.Title,
            meta.Count == 0 ? null : string.Join(" · ", meta));
    }


    private ThinHtml Results(
        string heading,
        BookSearchResult result,
        string query,
        string? q,
        string? genre,
        string basePath = "/m/search",
        SearchWithEntities? entities = null)
    {
        var page = new ThinHtml($"{heading} — {CollectionTitle}")
            .Header(heading, $"{result.Total:N0} found", ("/m", "Home"), ("/m/genres", "Genres"));

        if (basePath == "/m/search")
        {
            var hidden = string.IsNullOrWhiteSpace(genre)
                ? null
                : new[] { ("genre", genre) };
            page.SearchForm("/m/search", q, hidden);
        }

        if (result.Total == 0)
        {
            page.Note("Nothing found.");
            return page;
        }

        // When the query names an author or a series, offer the entity itself before the
        // books: someone typing an author's surname usually wants their shelf, not page 1
        // of 241 loose titles.
        if (result.Page == 1 && entities is not null)
        {
            if (entities.Authors.Count > 0)
            {
                page.Caption(entities.Authors.Count == 1 ? "Author" : "Authors");
                page.List(entities.Authors.Select(hit => ThinHtml.Row(
                    $"/m/author?name={Uri.EscapeDataString(hit.Name)}",
                    hit.Name, $"{hit.Count:N0} books here", "›")));
            }

            if (entities.Series.Count > 0)
            {
                page.Caption(entities.Series.Count == 1 ? "Series" : "Series matches");
                page.List(entities.Series.Select(hit => ThinHtml.Row(
                    $"/m/series?name={Uri.EscapeDataString(hit.Name)}",
                    hit.Name, $"{hit.Count:N0} books here", "›")));
            }

            if (entities.Authors.Count > 0 || entities.Series.Count > 0) page.Caption("Books");
        }

        page.List(result.Books.Select(BookRow));

        var separator = string.IsNullOrEmpty(query) ? "?" : "&";
        page.Pager(
            result.HasPreviousPage ? $"{basePath}{query}{separator}page={result.Page - 1}" : null,
            result.HasNextPage ? $"{basePath}{query}{separator}page={result.Page + 1}" : null,
            $"{result.Page} / {result.TotalPages:N0}");

        return page;
    }

    /// <summary>Query string of the non-empty values, starting with '?'.</summary>
    private static string BuildQuery(params (string Name, string? Value)[] values)
    {
        var parts = values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => $"{v.Name}={Uri.EscapeDataString(v.Value!)}")
            .ToList();

        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    /// <summary>
    /// First boot takes minutes, so the wait is made legible instead of blank: a progress
    /// bar, the current phase, and a meta refresh so the page advances on its own. No
    /// JavaScript involved — the reader's browser does the polling.
    /// </summary>
    private IActionResult NotReadyPage()
    {
        var snapshot = state.Snapshot();
        var failed = snapshot.Error is not null;

        var page = new ThinHtml(CollectionTitle)
            .Header(CollectionTitle, failed ? "indexing failed" : "preparing the library");

        if (failed)
        {
            page.Note(snapshot.Error!)
                .Note("Check the server log. The page will not refresh by itself.")
                .Raw(ThinHtml.Link("/m", "Try again", "d"));

            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Content(page.Render(), "text/html; charset=utf-8", Encoding.UTF8);
        }

        page.AutoRefresh(RefreshSeconds)
            .Big($"{snapshot.Percent}%")
            .ProgressBar(snapshot.Percent)
            .Note(snapshot.Message)
            .Caption("Status")
            .List(
            [
                ThinHtml.Row("/m", "Phase", snapshot.Phase, null),
                ThinHtml.Row("/m", "Refreshing", $"every {RefreshSeconds}s", null),
            ]);

        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        Response.Headers.RetryAfter = RefreshSeconds.ToString();
        return Content(page.Render(), "text/html; charset=utf-8", Encoding.UTF8);
    }

    private ContentResult Html(ThinHtml page) =>
        Content(page.Render(), "text/html; charset=utf-8", Encoding.UTF8);
}
