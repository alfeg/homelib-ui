using CsvHelper;
using CsvHelper.Configuration;

namespace MyHomeLib.Library;

public class InpReader : IDisposable
{
    public readonly string INPX_ITEM_DELIMITER = "" + (char)4;
    public const string INPX_SUBITEM_DELIMITER = ",";
    public readonly string EOT = $"{(char)4}";
    public InpReader(Stream stream)
    {
        Stream = stream;
    }

    public Stream Stream { get; }

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

    private void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            Stream.Dispose();
        }

        disposed = true;
    }

    bool disposed = false;
    public void Dispose()
    {
        // Dispose of unmanaged resources.
        Dispose(true);
        // Suppress finalization.
        GC.SuppressFinalize(this);
    }
}
