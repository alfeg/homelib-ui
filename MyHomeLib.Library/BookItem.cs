namespace MyHomeLib.Library;

public interface IBookItem
{
    int Id { get; }
    string ArchiveFile { get; }
    string File { get; }
    string Ext { get;  }
}

public class BookItem : IBookItem
{
    public int Id { get; set; }
    public string Authors { get; set; }

    public string Genre { get; set; }

    public string Title { get; set; }

    public string Series { get; set; }

    public string SeriesNo { get; set; }    

    public string ArchiveFile { get; set; }
    public string File { get; set; }
    
    public string Ext { get; set; }
    public DateOnly Date { get; set; }
    public long Size { get; set; }

    public string Lang { get; set; }

    public bool Deleted { get; set; }
        
    public string LibRate { get; set; }

    public string Keywords { get; set; }
}
