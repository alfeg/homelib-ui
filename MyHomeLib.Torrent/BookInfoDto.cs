namespace MyHomeListServer.Torrent;

public record BookInfoDto
{
    public int Id { get; init; }
    public string Title { get; init; }
    public string Author { get; init; }
    public string UrlPart { get; init; }
    public string Series { get; set; }
};