using System.Text.Json.Serialization;

namespace MyHomeLib.Web.Services.TorrServe;

internal class TorrServeData
{
    [JsonPropertyName("TorrServer")] public TorrServerSection? TorrServer { get; set; }
}