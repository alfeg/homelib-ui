namespace MyHomeLib.Files.Torrents;

public record DownloadResponse(byte[] Data, string ContentType, string Name);