namespace MyHomeLib.Web.Services.Models;

public record DownloadResponse(byte[] Data, string ContentType, string Name);
