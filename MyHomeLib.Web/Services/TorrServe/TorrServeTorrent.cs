using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyHomeLib.Web.Services.TorrServe;

public class TorrServeTorrent
{
    [JsonPropertyName("hash")]        public string Hash       { get; set; } = "";
    [JsonPropertyName("title")]       public string Title      { get; set; } = "";
    [JsonPropertyName("stat")]        public int    Stat       { get; set; }
    [JsonPropertyName("stat_string")] public string StatString { get; set; } = "";
    [JsonPropertyName("name")]        public string Name       { get; set; } = "";

    // Transfer stats (top-level in TorrentStatus)
    [JsonPropertyName("download_speed")]         public double DownloadSpeed       { get; set; }
    [JsonPropertyName("upload_speed")]           public double UploadSpeed         { get; set; }
    [JsonPropertyName("total_peers")]            public int    TotalPeers          { get; set; }
    [JsonPropertyName("pending_peers")]          public int    PendingPeers        { get; set; }
    [JsonPropertyName("active_peers")]           public int    ActivePeers         { get; set; }
    [JsonPropertyName("half_open_peers")]        public int    HalfOpenPeers       { get; set; }
    [JsonPropertyName("connected_seeders")]      public int    ConnectedSeeders    { get; set; }
    [JsonPropertyName("loaded_size")]            public long   LoadedSize          { get; set; }
    [JsonPropertyName("torrent_size")]           public long   TorrentSize         { get; set; }
    [JsonPropertyName("preloaded_bytes")]        public long   PreloadedBytes      { get; set; }
    [JsonPropertyName("preload_size")]           public long   PreloadSize         { get; set; }
    [JsonPropertyName("bytes_written")]          public long   BytesWritten        { get; set; }
    [JsonPropertyName("bytes_written_data")]     public long   BytesWrittenData    { get; set; }
    [JsonPropertyName("bytes_read")]             public long   BytesRead           { get; set; }
    [JsonPropertyName("bytes_read_data")]        public long   BytesReadData       { get; set; }
    [JsonPropertyName("bytes_read_useful_data")] public long   BytesReadUsefulData { get; set; }
    [JsonPropertyName("chunks_written")]         public long   ChunksWritten       { get; set; }
    [JsonPropertyName("chunks_read")]            public long   ChunksRead          { get; set; }
    [JsonPropertyName("chunks_read_useful")]     public long   ChunksReadUseful    { get; set; }
    [JsonPropertyName("chunks_read_wasted")]     public long   ChunksReadWasted    { get; set; }
    [JsonPropertyName("pieces_dirtied_good")]    public long   PiecesDirtiedGood   { get; set; }
    [JsonPropertyName("pieces_dirtied_bad")]     public long   PiecesDirtiedBad    { get; set; }
    [JsonPropertyName("duration_seconds")]       public double DurationSeconds     { get; set; }
    [JsonPropertyName("bit_rate")]               public string BitRate             { get; set; } = "";

    // TorrServe v1: files come in a top-level file_stat array
    [JsonPropertyName("file_stat")]  public TorrServeFile[]? FileStat { get; set; }

    // TorrServe v2+: files are inside data → "{\"TorrServer\":{\"Files\":[...]}}"
    [JsonPropertyName("data")]       public string? DataJson  { get; set; }

    /// <summary>Returns the file list from whichever field TorrServe populated.</summary>
    [JsonIgnore]
    public TorrServeFile[]? Files
    {
        get
        {
            if (FileStat is { Length: > 0 })
                return FileStat;

            if (string.IsNullOrEmpty(DataJson))
                return null;

            try
            {
                var data = JsonSerializer.Deserialize(DataJson, TorrServeJsonContext.Default.TorrServeData);
                return data?.TorrServer?.Files;
            }
            catch { return null; }
        }
    }
}