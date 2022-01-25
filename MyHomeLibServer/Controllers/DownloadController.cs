using Microsoft.AspNetCore.Mvc;
using MyHomeLibServer.Data;

namespace MyHomeLibServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DownloadController : Controller
    {
        private readonly LibraryAccessor libraryAccessor;

        public DownloadController(LibraryAccessor libraryAccessor)
        {
            this.libraryAccessor = libraryAccessor;
        }

        [HttpGet]
        [Route("{id}")]
        public IActionResult Index(long id)
        {
            var book = libraryAccessor.Library.OpenBook(id, out var bookItem);
            return File(book, "application/" + bookItem.Ext, bookItem.Title + "." + bookItem.Ext, false);
        }
    }
}
