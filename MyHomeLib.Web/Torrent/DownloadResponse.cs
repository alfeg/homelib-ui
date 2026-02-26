namespace MyHomeLib.Web;

public record DownloadResponse(byte[] Data, string ContentType, string Name);
