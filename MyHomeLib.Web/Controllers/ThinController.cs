using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Models.Thin;
using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web.Controllers;

/// <summary>
/// Server-rendered pages for low-power devices — the reader's own browser, an old phone,
/// anything that cannot afford a SPA. No JavaScript is served by any route here.
/// </summary>
/// <remarks>
/// The controller only builds view models; the markup lives in Views/Thin. Razor escapes
/// every interpolated value by default, which matters more than it looks: these pages render
/// titles, author names and series straight out of an untrusted 586k-row collection, and the
/// previous hand-rolled builder relied on remembering to escape each one.
/// </remarks>
[Route("m")]
public class ThinController(
    LibrarySearchService search,
    GenreLabelService genreLabels,
    IndexState state,
    IOptions<LibraryConfig> libraryConfig) : Controller
{
    private const int PageSize = 20;

    /// <summary>How often the indexing page reloads itself while the library is building.</summary>
    private const int RefreshSeconds = 5;

    private static readonly Crumb Home = new("/m", "Home");
    private static readonly Crumb GenresCrumb = new("/m/genres", "Genres");

    private readonly LibraryConfig _config = libraryConfig.Value;

    private string CollectionTitle => state.CollectionTitle ?? _config.Title;

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!state.IsReady) return NotReadyPage();

        return View(new IndexModel
        {
            Title = CollectionTitle,
            Heading = CollectionTitle,
            Subtitle = $"{state.TotalBooks:N0} books",
            TotalBooks = state.TotalBooks,
            Sections =
            [
                new RowItem("/m/genres", "By genre", null, "›"),
                new RowItem("/m/new", "Recently added", null, "›"),
                new RowItem("/m/all", "All books", $"{state.TotalBooks:N0} titles", "›"),
            ],
        });
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
            ? string.IsNullOrWhiteSpace(q) ? "All books" : $"“{q}”"
            : genreLabels.Label(genre);

        var query = BuildQuery(("q", q), ("genre", genre));
        var showCards = found.Books.Page == 1;

        return View("Results", new ResultsModel
        {
            Title = $"{heading} — {CollectionTitle}",
            Heading = heading,
            Subtitle = $"{found.Books.Total:N0} found",
            Crumbs = [Home, GenresCrumb],
            Result = found.Books,
            Books = [.. found.Books.Books.Select(BookRow)],
            SearchAction = "/m/search",
            Query = q,
            HiddenFields = string.IsNullOrWhiteSpace(genre) ? [] : [("genre", genre)],
            AuthorCards = showCards ? [.. found.Authors.Select(AuthorCard)] : [],
            SeriesCards = showCards ? [.. found.Series.Select(SeriesCard)] : [],
            PreviousHref = Paged("/m/search", query, found.Books.Page - 1, found.Books.HasPreviousPage),
            NextHref = Paged("/m/search", query, found.Books.Page + 1, found.Books.HasNextPage),
            PagerCaption = $"{found.Books.Page} / {found.Books.TotalPages:N0}",
        });
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

        return View("Results", new ResultsModel
        {
            Title = $"Recently added — {CollectionTitle}",
            Heading = "Recently added",
            Subtitle = $"{result.Total:N0} found",
            Crumbs = [Home, GenresCrumb],
            Result = result,
            Books = [.. result.Books.Select(BookRow)],
            PreviousHref = Paged("/m/new", "", result.Page - 1, result.HasPreviousPage),
            NextHref = Paged("/m/new", "", result.Page + 1, result.HasNextPage),
            PagerCaption = $"{result.Page} / {result.TotalPages:N0}",
        });
    }

    /// <summary>
    /// Two levels: categories first, then the genres inside one. A flat list of ~278 genre
    /// entries is a punishing scroll on an e-ink page-turn device.
    /// </summary>
    [HttpGet("genres")]
    public async Task<IActionResult> Genres([FromQuery] string? category, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();

        var categories = genreLabels.Categorise(await search.GenreCountsAsync(ct));

        if (string.IsNullOrWhiteSpace(category))
        {
            return View("Rows", new RowsModel
            {
                Title = $"Genres — {CollectionTitle}",
                Heading = "By genre",
                Crumbs = [Home],
                Rows =
                [
                    .. categories.Select(c => new RowItem(
                        $"/m/genres?category={Uri.EscapeDataString(c.Name)}",
                        c.Name, $"{c.Count:N0} books", "›")),
                ],
            });
        }

        var picked = categories.FirstOrDefault(c =>
            string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase));

        if (picked is null) return NotFound();

        return View("Rows", new RowsModel
        {
            Title = $"{picked.Name} — {CollectionTitle}",
            Heading = picked.Name,
            Subtitle = $"{picked.Count:N0} books",
            Crumbs = [Home, GenresCrumb],
            Rows =
            [
                .. picked.Genres.Select(g => new RowItem(
                    $"/m/search?genre={Uri.EscapeDataString(g.Code)}",
                    g.Label, $"{g.Count:N0} books", "›")),
            ],
        });
    }

    /// <summary>
    /// Everything by one author: their series first, then the books themselves. The series
    /// list comes from the same query as the books, so this costs one search, not two.
    /// </summary>
    [HttpGet("author")]
    public async Task<IActionResult> Author(
        [FromQuery] string name, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReadyPage();
        if (string.IsNullOrWhiteSpace(name)) return NotFound();

        var author = await search.AuthorAsync(name, page, PageSize, ct);
        var query = BuildQuery(("name", name));

        return View(new AuthorModel
        {
            Title = $"{name} — {CollectionTitle}",
            Heading = name,
            Subtitle = $"{author.Books.Total:N0} books",
            Crumbs = [Home],
            Series =
            [
                .. author.Series.Select(entry => new RowItem(
                    $"/m/series?name={Uri.EscapeDataString(entry.Name)}&author={Uri.EscapeDataString(name)}",
                    entry.Name,
                    entry.YearRange.Length > 0
                        ? $"{entry.Count:N0} books · added {entry.YearRange}"
                        : $"{entry.Count:N0} books",
                    "›")),
            ],
            Books = [.. author.Books.Books.Select(BookRow)],
            PreviousHref = Paged("/m/author", query, author.Books.Page - 1, author.Books.HasPreviousPage),
            NextHref = Paged("/m/author", query, author.Books.Page + 1, author.Books.HasNextPage),
            PagerCaption = $"{author.Books.Page} / {author.Books.TotalPages:N0}",
        });
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

        var query = BuildQuery(("name", name), ("author", author));

        return View("Rows", new RowsModel
        {
            Title = $"{name} — {CollectionTitle}",
            Heading = name,
            Subtitle = $"{result.Total:N0} books",
            Crumbs = string.IsNullOrWhiteSpace(author)
                ? [Home]
                : [Home, new Crumb($"/m/author?name={Uri.EscapeDataString(author)}", author)],
            Rows =
            [
                .. result.Books.Select(book => new RowItem(
                    $"/m/book/{book.Uid}",
                    book.SeriesIndex is { } index ? $"{index}. {book.Title}" : book.Title,
                    string.IsNullOrWhiteSpace(book.Authors) ? null : book.Authors)),
            ],
            PreviousHref = Paged("/m/series", query, result.Page - 1, result.HasPreviousPage),
            NextHref = Paged("/m/series", query, result.Page + 1, result.HasNextPage),
            PagerCaption = $"{result.Page} / {result.TotalPages:N0}",
        });
    }

    [HttpGet("book/{uid}")]
    public async Task<IActionResult> Book(string uid, CancellationToken ct)
    {
        if (!state.IsReady) return NotReadyPage();

        var book = await search.GetBookAsync(uid, ct);
        if (book is null) return NotFound();

        return View(new BookModel
        {
            Title = $"{book.Title} — {CollectionTitle}",
            Heading = string.IsNullOrWhiteSpace(book.Title) ? book.File : book.Title,
            Subtitle = string.IsNullOrWhiteSpace(book.Authors) ? null : book.Authors,
            Crumbs = [Home, GenresCrumb],
            Book = book,
            SeriesRows = string.IsNullOrWhiteSpace(book.Series)
                ? []
                : [new RowItem($"/m/series?name={Uri.EscapeDataString(book.Series)}", book.SeriesDisplay(), null, "›")],
            AuthorRows =
            [
                .. book.AuthorList.Select(a => new RowItem(
                    $"/m/author?name={Uri.EscapeDataString(a)}", a, null, "›")),
            ],
            GenreRows =
            [
                .. book.Genres.Select(g => new RowItem(
                    $"/m/search?genre={Uri.EscapeDataString(g)}",
                    genreLabels.Label(g),
                    genreLabels.Category(g) is { Length: > 0 } category ? category : null,
                    "›")),
            ],
            Facts =
            [
                ("Format", $".{book.Ext}"),
                ("Size", book.Size > 0 ? $"{book.Size / 1024:N0} KB" : "—"),
                ("Language", string.IsNullOrWhiteSpace(book.Lang) ? "—" : book.Lang),
                // Labelled "added" deliberately: the INPX date records when the book entered
                // the collection, not when it was written or published.
                ("Added to library", book.Added ?? book.Year?.ToString() ?? "—"),
                ("Library id", book.Id > 0 ? book.Id.ToString() : "—"),
            ],
        });
    }

    // ── Building blocks ──────────────────────────────────────────────────────

    private static RowItem BookRow(BookDocument book)
    {
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(book.Authors)) meta.Add(book.Authors);
        if (!string.IsNullOrWhiteSpace(book.Series)) meta.Add(book.SeriesDisplay());

        return new RowItem(
            $"/m/book/{book.Uid}",
            string.IsNullOrWhiteSpace(book.Title) ? book.File : book.Title,
            meta.Count == 0 ? null : string.Join(" · ", meta));
    }

    private static RowItem AuthorCard(EntityHit hit) => new(
        $"/m/author?name={Uri.EscapeDataString(hit.Name)}", hit.Name, $"{hit.Count:N0} books here", "›");

    private static RowItem SeriesCard(EntityHit hit) => new(
        $"/m/series?name={Uri.EscapeDataString(hit.Name)}", hit.Name, $"{hit.Count:N0} books here", "›");

    private static string? Paged(string basePath, string query, int page, bool exists)
    {
        if (!exists) return null;

        var separator = string.IsNullOrEmpty(query) ? "?" : "&";
        return $"{basePath}{query}{separator}page={page}";
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

    private IActionResult NotReadyPage()
    {
        var snapshot = state.Snapshot();
        var failed = snapshot.Error is not null;

        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        if (!failed) Response.Headers.RetryAfter = RefreshSeconds.ToString();

        return View("NotReady", new NotReadyModel
        {
            Title = CollectionTitle,
            Heading = CollectionTitle,
            Subtitle = failed ? "indexing failed" : "preparing the library",
            AutoRefreshSeconds = failed ? 0 : RefreshSeconds,
            Status = snapshot,
            RefreshSeconds = RefreshSeconds,
        });
    }
}
