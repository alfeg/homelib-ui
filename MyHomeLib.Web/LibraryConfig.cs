namespace MyHomeLib.Web;

public class LibraryConfig
{
    /// <summary>
    /// Path to the .inpx file.
    /// Set via appsettings.json ("Library:InpxPath"), environment variable
    /// (Library__InpxPath), or command-line argument (--Library:InpxPath=&lt;path&gt;).
    /// </summary>
    public string InpxPath { get; set; } = string.Empty;
}
