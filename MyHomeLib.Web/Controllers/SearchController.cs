using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web.Controllers;

/// <summary>JSON search API. Backs the SPA; also useful for scripting the library.</summary>
[ApiController]
[Route("api")]
[EnableCors("Api")]
public class SearchController(LibrarySearchService search, IndexState state) : ControllerBase
{
    [HttpGet("status")]
    public IndexStatusSnapshot Status() => state.Snapshot();

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery(Name = "genre")] string[]? genres,
        [FromQuery] string? lang,
        [FromQuery] int? yearFrom,
        [FromQuery] int? yearTo,
        [FromQuery] string? author,
        [FromQuery] int page = 1,
        [FromQuery] int size = 30,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReady();

        var found = await search.SearchWithEntitiesAsync(new BookQuery(
            Q: q,
            Genres: genres,
            Lang: lang,
            YearFrom: yearFrom,
            YearTo: yearTo,
            Author: author,
            Page: page,
            PageSize: size,
            NewestFirst: string.Equals(sort, "new", StringComparison.OrdinalIgnoreCase)), ct);

        var result = found.Books;

        return Ok(new
        {
            books = result.Books,
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages,
            genreCounts = result.GenreCounts,
            langCounts = result.LangCounts,
            authorHits = found.Authors,
            seriesHits = found.Series,
        });
    }

    [HttpGet("genres")]
    public async Task<IActionResult> Genres(CancellationToken ct)
    {
        if (!state.IsReady) return NotReady();
        return Ok(await search.GenreCountsAsync(ct));
    }

    [HttpGet("author")]
    public async Task<IActionResult> Author([FromQuery] string name, [FromQuery] int page = 1,
        [FromQuery] int size = 30, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReady();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required.");

        var author = await search.AuthorAsync(name, page, size, ct);

        return Ok(new
        {
            author = author.Author,
            series = author.Series,
            books = author.Books.Books,
            total = author.Books.Total,
            page = author.Books.Page,
            totalPages = author.Books.TotalPages,
        });
    }

    [HttpGet("series")]
    public async Task<IActionResult> Series([FromQuery] string name, [FromQuery] string? author,
        [FromQuery] int page = 1, [FromQuery] int size = 30, CancellationToken ct = default)
    {
        if (!state.IsReady) return NotReady();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required.");

        var result = await search.SearchAsync(new BookQuery(
            Series: name, Author: author, Page: page, PageSize: size, SeriesOrder: true), ct);

        return Ok(new { series = name, books = result.Books, total = result.Total, totalPages = result.TotalPages });
    }

    [HttpGet("book/{uid}")]
    public async Task<IActionResult> Book(string uid, CancellationToken ct)
    {
        if (!state.IsReady) return NotReady();

        var book = await search.GetBookAsync(uid, ct);
        return book is null ? NotFound() : Ok(book);
    }

    private IActionResult NotReady() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, state.Snapshot());
}
