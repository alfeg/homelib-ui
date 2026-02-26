using System.Text.Json.Serialization;

namespace MyHomeLib.Web.Services.TorrServe;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TorrentAddRequest))]
[JsonSerializable(typeof(TorrentActionRequest))]
[JsonSerializable(typeof(TorrServeTorrent))]
[JsonSerializable(typeof(TorrServeFile))]
[JsonSerializable(typeof(TorrServeData))]
internal partial class TorrServeJsonContext : JsonSerializerContext { }