using MyHomeLib.Library;

namespace MyHomeLib.Web;

public sealed class SearchPageState
{
    public string Query { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public (IReadOnlyList<BookItem> Page, int Total)? Results { get; set; }
    public long? SearchMs { get; set; }

    public void Clear()
    {
        Query = string.Empty;
        Language = string.Empty;
        Results = null;
        SearchMs = null;
    }
}
