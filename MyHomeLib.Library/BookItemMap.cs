using CsvHelper.Configuration;

namespace MyHomeLib.Library;

public class BookItemMap : ClassMap<BookItem>
{
    public BookItemMap()
    {
        Map(m => m.Authors).Index(0);//.AsArray(0);
        Map(m => m.Genre).Index(1);
        Map(m => m.Title).Index(2);
        Map(m => m.Series).Index(3);
        Map(m => m.SeriesNo).Index(4);
        Map(m => m.File).Index(5);
        Map(m => m.Size).Index(6);
        Map(m => m.Id).Index(7);
        Map(m => m.Deleted).Convert(row => row.Row.GetField(8) == "1").Index(8);
        Map(m => m.Ext).Index(9);
        Map(m => m.Date).Index(10);
        Map(m => m.Lang).Index(11);
        Map(m => m.LibRate).Index(12).Default(string.Empty);
        Map(m => m.Keywords).Index(13);        
    }
}
