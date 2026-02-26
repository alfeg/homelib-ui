using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MyHomeLib.Web;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Models;

namespace MyHomeLib.Web.Controllers;

[ApiController]
[Route("api/library")]
[EnableCors("Api")]
public class LibraryController(
    DownloadManager downloadManager,
    IdleTorrentCleanupService idleTorrentCleanupService,
    ILogger<LibraryController> logger) : ControllerBase
{
    [HttpPost("inpx")]
    public async Task<IActionResult> GetInpx(LibraryBooksRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MagnetUri))
            return BadRequest(L("magnetUri is required.", "Требуется magnetUri."));

        idleTorrentCleanupService.MarkActivity(request.MagnetUri);

        if (request.ForceReindex)
            logger.LogDebug("forceReindex is ignored for /api/library/inpx");

        try
        {
            var inpxFile = await downloadManager.GetInpxFileAsync(request.MagnetUri, ct);
            return File(inpxFile.Data, "application/octet-stream", inpxFile.FileName);
        }
        catch (FormatException)
        {
            return BadRequest(L("Invalid magnetUri.", "Некорректный magnetUri."));
        }
        catch (InvalidOperationException)
        {
            return BadRequest(L("Unable to prepare INPX file.", "Не удалось подготовить INPX файл."));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                L("TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                L("TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."));
        }
        catch (TimeoutException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                L("TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in /api/library/inpx");
            return StatusCode(StatusCodes.Status500InternalServerError,
                L("Internal server error.", "Внутренняя ошибка сервера."));
        }
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download(LibraryDirectDownloadRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MagnetUri)
            || string.IsNullOrWhiteSpace(request.ArchiveFile)
            || string.IsNullOrWhiteSpace(request.File)
            || string.IsNullOrWhiteSpace(request.Ext))
        {
            return BadRequest(L("magnetUri, archiveFile, file and ext are required.", "Требуются поля magnetUri, archiveFile, file и ext."));
        }

        idleTorrentCleanupService.MarkActivity(request.MagnetUri);

        try
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
        }
        catch (FormatException ex)
        {
            return BadRequest(string.IsNullOrWhiteSpace(ex.Message)
                ? L("Invalid request.", "Некорректный запрос.")
                : ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(string.IsNullOrWhiteSpace(ex.Message)
                ? L("File not found.", "Файл не найден.")
                : ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(string.IsNullOrWhiteSpace(ex.Message)
                ? L("Unable to process download request.", "Не удалось обработать запрос на скачивание.")
                : ex.Message);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                L("TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                L("TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."));
        }
        catch (TimeoutException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                L("TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in /api/library/download");
            return StatusCode(StatusCodes.Status500InternalServerError,
                L("Internal server error.", "Внутренняя ошибка сервера."));
        }
    }

    private string L(string en, string ru)
    {
        var lang = HttpContext.Request.Headers.AcceptLanguage.ToString();
        return lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? ru : en;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
