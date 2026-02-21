namespace MyHomeLib.Web;

public enum DownloadStatus { Pending, Downloading, Ready, Failed }

public class DownloadJob
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public int    BookId      { get; set; }
    public string Title       { get; set; } = string.Empty;
    public string Authors     { get; set; } = string.Empty;
    public string Archive     { get; set; } = string.Empty;
    public string FileName    { get; set; } = string.Empty;  // file inside archive, e.g. 123456.fb2
    public DownloadStatus Status      { get; set; } = DownloadStatus.Pending;
    public string? Error      { get; set; }
    public string? FilePath   { get; set; }  // absolute path to saved file
    public string? DownloadName { get; set; } // friendly name served to browser
    public string? ContentType  { get; set; }
    public DateTime AddedAt     { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
