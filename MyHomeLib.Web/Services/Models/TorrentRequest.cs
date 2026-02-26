namespace MyHomeLib.Web.Services.Models;

public record TorrentRequest(string Library)
{
    /// <summary>Full magnet URI. Required for AddTorrentAsync calls.</summary>
    public string? MagnetUri { get; set; }
};
