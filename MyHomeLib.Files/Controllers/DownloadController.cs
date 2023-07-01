using Fb2.Document;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MyHomeLib.Files.Core;
using MyHomeLib.Files.Torrents;

[Route("/get")]
[ApiController]
public class DownloadController : Controller
{
    private readonly DownloadManager _torrentService;
    
    public DownloadController(DownloadManager torrentService)
    {
        _torrentService = torrentService;
    }
    //
    // [Route("{libraryInfoHash}/search")]
    // public async Task<ActionResult> SearchBook(string libraryInfoHash, [FromQuery] string book)
    // {
    // }

    [Route("{libraryInfoHash}/{archive}/{book}")]
    public async Task<ActionResult> DownloadBook(string libraryInfoHash, string archive, string book)
    {
        // http://localhost:5228/get/4b4c53cd8cfd993e8a31c564fa80b853422cdf48/f.fb2-183654-185837.zip/183671.fb2

        var response = await _torrentService.DownloadFile(new DownloadRequest(libraryInfoHash, archive, book));

        return this.File(response.Data, response.ContentType, response.Name);
        
    }
}