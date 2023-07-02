using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using MyHomeLib.Files.Torrents;

namespace MyHomeLib.Files.Controllers;

[Route("/get")]
[ApiController]
public class DownloadController : Controller
{
    private readonly DownloadManager _downloadManager;
    private readonly LibraryIndexer _libraryIndexer;

    public DownloadController(DownloadManager downloadManager, LibraryIndexer libraryIndexer)
    {
        _downloadManager = downloadManager;
        _libraryIndexer = libraryIndexer;
    }
    
    [Route("{libraryInfoHash}/search")]
    public async Task<ActionResult> SearchBook(string libraryInfoHash, [FromQuery] string book)
    {
        var displayUrl = Request.GetDisplayUrl();
        displayUrl = displayUrl.Substring(0, displayUrl.IndexOf("/get", StringComparison.Ordinal));
        var books = _libraryIndexer.SearchLibrary(libraryInfoHash, book, displayUrl);

        return Json(books.ToBlockingEnumerable()
            .Take(1000)
            .ToList());
    }

    [Route("{libraryInfoHash}/{archive}/{book}")]
    public async Task<ActionResult> DownloadBook(string libraryInfoHash, string archive, string book)
    {
        // http://localhost:5228/get/4b4c53cd8cfd993e8a31c564fa80b853422cdf48/f.fb2-183654-185837.zip/183671.fb2

        var response = await _downloadManager.DownloadFile(new DownloadRequest(libraryInfoHash, archive, book));

        return File(response.Data, response.ContentType, response.Name);
        
    }
}