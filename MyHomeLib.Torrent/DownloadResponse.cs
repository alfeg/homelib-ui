namespace MyHomeLib.Torrent;

public record DownloadResponse(byte[] Data, string ContentType, string Name);