namespace MyHomeListServer.Torrent;

public record SearchResponse(string[] Names) : TorrentResponse;