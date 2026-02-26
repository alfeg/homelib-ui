using System.Text.Json.Serialization;

namespace MyHomeLib.Web.Services.TorrServe;

public class TorrServeFile
{
    [JsonPropertyName("id")]     public int    Id     { get; set; }
    [JsonPropertyName("path")]   public string Path   { get; set; } = "";
    [JsonPropertyName("length")] public long   Length { get; set; }
}