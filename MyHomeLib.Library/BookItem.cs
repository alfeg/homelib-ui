namespace MyHomeLib.Library;

public class BookItem
{
    public long Id { get; set; }
    public string Authors { get; set; }

    public string Genre { get; set; }

    public string Title { get; set; }

    public string Series { get; set; }

    public string SeriesNo { get; set; }    

    public string ArchiveFile { get; set; }
    public string File { get; set; }
    
    public string Ext { get; set; }
    public string Date { get; set; }
    public long Size { get; set; }

    public string Lang { get; set; }

    public bool Deleted { get; set; }
        
    public string LibRate { get; set; }

    public string Keywords { get; set; }

}
