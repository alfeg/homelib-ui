namespace MyHomeLib.Files.Torrents;

public record BookInfoDto
{
    public string Title { get; init; }
    public string Author { get; init; }
    public string UrlPart { get; init; }
    public string Series { get; set; }
};