using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyHomeLibServer.Data;
using MyHomeLibServer.Data.Services;

namespace MyHomeLibServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DownloadController : Controller
    {
        private readonly LibraryAccessor libraryAccessor;
        private readonly IDbContextFactory<LibDbContext> dbContextFactory;

        public DownloadController(LibraryAccessor libraryAccessor, IDbContextFactory<LibDbContext> dbContextFactory)
        {
            this.libraryAccessor = libraryAccessor;
            this.dbContextFactory = dbContextFactory;
        }

        [HttpGet]
        [Route("{id}")]
        public IActionResult Index(int id)
        {
            using var db = dbContextFactory.CreateDbContext();
            var bookItem = db.Books.Find(id);
            var book = libraryAccessor.Library.OpenBook(bookItem);
            return File(book, "application/" + bookItem.Extension, bookItem.Title + "." + bookItem.Extension, false);
        }
    }
}
