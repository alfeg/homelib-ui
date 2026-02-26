using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Models;

namespace MyHomeLib.Web.Controllers;

[ApiController]
[Route("api/library")]
[EnableCors("Api")]
public class LibraryController(
    DownloadManager downloadManager,
    IStringLocalizer<LibraryController> localizer,
    ILogger<LibraryController> logger) : ControllerBase
{
    [HttpPost("inpx")]
    public async Task<IActionResult> GetInpx(LibraryBooksRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MagnetUri))
            return BadRequest(localizer["magnetUri is required."]);

        return await ExecuteAsync(ct, async () =>
        {
            var inpxFile = await downloadManager.GetInpxFileAsync(request.MagnetUri, ct);
            return File(inpxFile.Data, "application/octet-stream", inpxFile.FileName);
        },
        ex => ex switch
        {
            FormatException           => BadRequest(localizer["Invalid magnetUri."]),
            InvalidOperationException => BadRequest(localizer["Unable to prepare INPX file."]),
            _                         => null
        });
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download(LibraryDirectDownloadRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MagnetUri)
            || string.IsNullOrWhiteSpace(request.ArchiveFile)
            || string.IsNullOrWhiteSpace(request.File)
            || string.IsNullOrWhiteSpace(request.Ext))
        {
            return BadRequest(localizer["magnetUri, archiveFile, file and ext are required."]);
        }

        return await ExecuteAsync(ct, async () =>
        {
            var hash = MagnetUriHelper.ParseInfoHash(request.MagnetUri);
            var ext = request.Ext.TrimStart('.');
            var fileName = $"{request.File}.{ext}";

            var response = await downloadManager.DownloadFile(
                new DownloadRequest(hash, request.ArchiveFile, fileName) { MagnetUri = request.MagnetUri }, ct);

            var contentType = string.IsNullOrWhiteSpace(response.ContentType)
                ? "application/octet-stream"
                : response.ContentType;

            var friendlyBase = string.IsNullOrWhiteSpace(request.Title)
                ? request.File
                : string.IsNullOrWhiteSpace(request.Authors)
                    ? request.Title
                    : $"{request.Authors} - {request.Title}";

            var downloadName = string.IsNullOrWhiteSpace(response.Name)
                ? MakeSafeFileName($"{friendlyBase}.{ext}")
                : response.Name;

            return File(response.Data, contentType, downloadName);
        },
        ex => ex switch
        {
            FormatException { Message: var m }           => BadRequest(string.IsNullOrWhiteSpace(m) ? localizer["Invalid request."] : m),
            FileNotFoundException { Message: var m }     => NotFound(string.IsNullOrWhiteSpace(m) ? localizer["File not found."] : m),
            InvalidOperationException { Message: var m } => BadRequest(string.IsNullOrWhiteSpace(m) ? localizer["Unable to process download request."] : m),
            _                                            => null
        });
    }

    /// <summary>
    /// Executes <paramref name="action"/>, first trying <paramref name="handleSpecific"/> for
    /// action-specific exceptions, then falling back to common TorrServe / unhandled error responses.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(
        CancellationToken ct,
        Func<Task<IActionResult>> action,
        Func<Exception, IActionResult?> handleSpecific)
    {
        try
        {
            return await action();
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Client disconnected — let the framework handle it
        }
        catch (Exception ex)
        {
            var specific = handleSpecific(ex);
            if (specific is not null) return specific;

            if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, localizer["TorrServe is unavailable. Please try again later."]);

            logger.LogError(ex, "Unhandled controller error");
            return StatusCode(StatusCodes.Status500InternalServerError, localizer["Internal server error."]);
        }
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

