using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Models;

namespace MyHomeLib.Web.Controllers;

/// <summary>
/// Legacy endpoints kept for the not-yet-migrated SPA.
/// </summary>
/// <remarks>
/// The magnet URI is now taken from <see cref="LibraryConfig"/> and any value sent by the
/// client is ignored: on this branch the instance serves exactly one operator-configured
/// library. That closes the open-relay hole the request-supplied magnet used to open.
/// </remarks>
[ApiController]
[Route("api/library")]
[EnableCors("Api")]
public class LibraryController(
    DownloadManager downloadManager,
    IOptions<LibraryConfig> libraryConfig,
    IStringLocalizer<LibraryController> localizer,
    ILogger<LibraryController> logger) : ControllerBase
{
    private readonly LibraryConfig _config = libraryConfig.Value;

    [HttpPost("inpx")]
    public async Task<IActionResult> GetInpx(CancellationToken ct)
    {
        if (!TryGetMagnet(out var magnetUri, out var failure)) return failure;

        return await ExecuteAsync(ct, async () =>
        {
            var inpxFile = await downloadManager.GetInpxFileAsync(magnetUri, ct);
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
        if (string.IsNullOrWhiteSpace(request.ArchiveFile)
            || string.IsNullOrWhiteSpace(request.File)
            || string.IsNullOrWhiteSpace(request.Ext))
        {
            return BadRequest(localizer["archiveFile, file and ext are required."]);
        }

        if (!TryGetMagnet(out var magnetUri, out var failure)) return failure;

        return await ExecuteAsync(ct, async () =>
        {
            var hash = MagnetUriHelper.ParseInfoHash(magnetUri);
            var ext = request.Ext.TrimStart('.');
            var fileName = $"{request.File}.{ext}";

            var response = await downloadManager.DownloadFile(
                new DownloadRequest(hash, request.ArchiveFile, fileName) { MagnetUri = magnetUri }, ct);

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

    private bool TryGetMagnet(out string magnetUri, out IActionResult failure)
    {
        magnetUri = _config.MagnetUri ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(magnetUri))
        {
            failure = null!;
            return true;
        }

        failure = StatusCode(StatusCodes.Status503ServiceUnavailable,
            localizer["This server has no library configured."]);
        return false;
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
