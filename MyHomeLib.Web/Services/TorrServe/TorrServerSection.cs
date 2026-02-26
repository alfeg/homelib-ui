using System.Text.Json.Serialization;

namespace MyHomeLib.Web.Services.TorrServe;

internal class TorrServerSection
{
    [JsonPropertyName("Files")] public TorrServeFile[]? Files { get; set; }
}