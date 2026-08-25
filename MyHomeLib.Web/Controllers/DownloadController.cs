using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web.Controllers;

/// <summary>
/// Plain GET download links. OPDS acquisition links and the e-ink pages both need a URL
/// that a dumb client can follow — no POST, no JSON body, no custom headers.
/// </summary>
[ApiController]
[EnableCors("Api")]
public class DownloadController(
    LibrarySearchService search,
    BookDownloadService downloads,
    IndexState state,
    ILogger<DownloadController> logger) : ControllerBase
{
    [HttpGet("/download/{uid}")]
    public Task<IActionResult> Raw(string uid, CancellationToken ct) => Serve(uid, zipped: false, ct);

    [HttpGet("/download/{uid}.zip")]
    public Task<IActionResult> Zipped(string uid, CancellationToken ct) => Serve(uid, zipped: true, ct);

    private async Task<IActionResult> Serve(string uid, bool zipped, CancellationToken ct)
    {
        if (!state.IsReady)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, $"Library is not ready: {state.Message}");

        var book = await search.GetBookAsync(uid, ct);
        if (book is null) return NotFound($"Unknown book {uid}.");

        try
        {
            var payload = await downloads.DownloadAsync(book, zipped, ct);
            return File(payload.Data, payload.ContentType, payload.FileName);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // client went away
        }
        catch (FileNotFoundException ex)
        {
            logger.LogWarning(ex, "[Download] {Uid} missing in archive", uid);
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            logger.LogWarning(ex, "[Download] TorrServe unavailable for {Uid}", uid);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "TorrServe is unavailable. Try again later.");
        }
    }
}
