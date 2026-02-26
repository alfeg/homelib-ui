namespace MyHomeLib.Web.Services.Models;

public record SearchRequest(string Library, string FilePattern) : TorrentRequest(Library);
