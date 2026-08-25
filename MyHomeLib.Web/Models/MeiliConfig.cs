namespace MyHomeLib.Web.Models;

public class MeiliConfig
{
    public string Url { get; set; } = "http://localhost:7700";

    /// <summary>Master or API key. Empty when Meilisearch runs without a key.</summary>
    public string? ApiKey { get; set; }

    public string IndexUid { get; set; } = "books";

    /// <summary>Index holding the one-document indexing state record.</summary>
    public string MetaIndexUid => $"{IndexUid}_meta";
}
