namespace MyHomeLibServer.Data;

public class LibraryConfig
{
    public const string Section = "Library";

    public string CatalogIndexFile { get; set; }
    public string DbPath { get; set; }
}
