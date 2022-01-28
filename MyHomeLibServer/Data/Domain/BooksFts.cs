namespace MyHomeLibServer.Data.Domain;

public class BooksFts
{
    public int RowId { get; set; }
    public Book Book { get; set; }
    public string Title { get; set; }
    public string Authors { get; set; }
    public string Keywords { get; set; }
    public string Series { get; set; }
    public string Match { get; set; }
    public double? Rank { get; set; }
}