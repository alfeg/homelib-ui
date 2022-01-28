namespace MyHomeLibServer.Data.Domain;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; }

    public string ArchiveFile { get; set; }
    public string FileName { get; set; }
    public string Extension { get; set; }
    public long Size { get; set; }
    public int? SeriesNo { get; set; }
    public string Language { get; set; }
    public DateOnly Date { get; set; }
    public Series? Series { get; set; }
    public List<Author> Authors { get; set; } = new();
    public List<Keyword> Keywords { get; set; } = new();
    public List<Genre> Genres { get; set; } = new();
    public BooksFts FTS { get; set; }
}