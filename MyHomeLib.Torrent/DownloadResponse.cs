namespace MyHomeListServer.Torrent;

public record DownloadResponse(byte[] Data, string ContentType, string Name, string FullPath) : TorrentResponse;