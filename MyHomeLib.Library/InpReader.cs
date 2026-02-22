using CsvHelper;
using CsvHelper.Configuration;

namespace MyHomeLib.Library;

public class InpReader(Stream stream) : IDisposable
{
    public const string INPX_ITEM_DELIMITER = "\x04";
    public const string INPX_SUBITEM_DELIMITER = ",";
    public const string EOT = "\x04";

    public Stream Stream { get; } = stream;

    public async IAsyncEnumerable<BookItem> ReadBooks()
    {
        using var sr = new StreamReader(Stream);
        using var csv = new CsvReader(sr, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Mode = CsvMode.NoEscape,
            NewLine = "\r\n",
            Delimiter = EOT,
        });
        csv.Context.RegisterClassMap<BookItemMap>();
        while (await csv.ReadAsync())
        {
            yield return csv.GetRecord<BookItem>();
        }
    }

    public void Dispose()
    {
        Stream.Dispose();
        GC.SuppressFinalize(this);
    }
}
