using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace MyHomeLib.Web;

/// <summary>
/// Thin client for the TorrServe HTTP API (https://github.com/YouROK/TorrServer).
/// </summary>
public class TorrServeClient(HttpClient http, string baseUrl, ILogger<TorrServeClient> logger)
{
    private readonly string _baseUrl = InitializeBaseUrl(baseUrl, logger);

    private static string InitializeBaseUrl(string baseUrl, ILogger logger)
    {
        // Normalize localhost → 127.0.0.1 to avoid IPv6 resolution issues on Windows
        var normalized = baseUrl.TrimEnd('/').Replace("://localhost:", "://127.0.0.1:", StringComparison.OrdinalIgnoreCase);
        logger.LogDebug("[TorrServe] Base URL: {Url}", normalized);
        return normalized;
    }

    /// <summary>Adds a torrent by magnet URI. Returns the info-hash hex string.</summary>
    public async Task<string> AddTorrentAsync(string magnetUri, string title, bool saveToDb = true,
        CancellationToken ct = default)
    {
        logger.LogDebug("[TorrServe] AddTorrent title={Title} saveToDb={Save}", title, saveToDb);
        var body = new TorrentAddRequest("add", magnetUri, title, saveToDb);
        var resp = await PostAsync("/torrents", body,
            TorrServeJsonContext.Default.TorrentAddRequest,
            TorrServeJsonContext.Default.TorrServeTorrent, ct);
        logger.LogDebug("[TorrServe] AddTorrent → hash={Hash} stat={Stat} ({StatStr})",
            resp.Hash, resp.Stat, resp.StatString);
        return resp.Hash;
    }

    /// <summary>
    /// Polls until TorrServe has fetched torrent metadata and returns the file list.
    /// </summary>
    public async Task<TorrServeFile[]> WaitForFilesAsync(string hash, CancellationToken ct = default)
    {
        logger.LogDebug("[TorrServe] WaitForFiles hash={Hash}", hash);
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var t = await GetTorrentAsync(hash, ct);
            // stat 2 = Torrent preload; stat 3 = Torrent working; stat 4 = Torrent closed; stat 5 = In DB
            // Files appear once metadata is resolved (file_stat is non-empty)
            if (t?.Files is { Length: > 0 })
            {
                logger.LogDebug("[TorrServe] WaitForFiles hash={Hash} resolved {Count} files after {Attempts} polls",
                    hash, t.Files.Length, attempt);
                return t.Files;
            }
            logger.LogDebug("[TorrServe] WaitForFiles hash={Hash} attempt={Attempt} stat={Stat} ({StatStr}) files={Files}",
                hash, ++attempt, t?.Stat, t?.StatString, t?.Files?.Length ?? 0);
            await Task.Delay(2000, ct);
        }
        ct.ThrowIfCancellationRequested();
        return [];
    }

    /// <summary>Returns current torrent info including file list (null if not found).</summary>
    public async Task<TorrServeTorrent?> GetTorrentAsync(string hash, CancellationToken ct = default)
    {
        try
        {
            var result = await PostAsync("/torrents", new TorrentActionRequest("get", hash),
                TorrServeJsonContext.Default.TorrentActionRequest,
                TorrServeJsonContext.Default.TorrServeTorrent, ct);
            logger.LogDebug("[TorrServe] GetTorrent hash={Hash} stat={Stat} ({StatStr}) files={Files}",
                hash, result.Stat, result.StatString, result.Files?.Length ?? 0);
            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("[TorrServe] GetTorrent hash={Hash} → not found: {Msg}", hash, ex.Message);
            return null;
        }
    }

    /// <summary>Removes a torrent from TorrServe (does not delete cached data).</summary>
    public async Task RemoveTorrentAsync(string hash, CancellationToken ct = default)
    {
        logger.LogDebug("[TorrServe] RemoveTorrent hash={Hash}", hash);
        await PostAsync("/torrents", new TorrentActionRequest("rem", hash),
            TorrServeJsonContext.Default.TorrentActionRequest,
            TorrServeJsonContext.Default.TorrServeTorrent, ct);
    }

    /// <summary>Returns the HTTP stream URL for a specific file index.</summary>
    public string GetStreamUrl(string hash, int fileIndex)
    {
        var url = $"{_baseUrl}/stream?link={hash}&index={fileIndex}&play";
        logger.LogDebug("[TorrServe] StreamUrl hash={Hash} index={Index} → {Url}", hash, fileIndex, url);
        return url;
    }

    /// <summary>Returns true if TorrServe is reachable (GET /echo). Also captures version string.</summary>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync(_baseUrl + "/echo", ct);
            if (!resp.IsSuccessStatusCode) return false;
            LastVersion = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Last server version string returned by /echo.</summary>
    public string? LastVersion { get; private set; }

    private async Task<TRes> PostAsync<TReq, TRes>(
        string path, TReq body, JsonTypeInfo<TReq> bodyInfo, JsonTypeInfo<TRes> resultInfo, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, bodyInfo);
        logger.LogDebug("[TorrServe] POST {Url} ← {Body}", _baseUrl + path, json);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(_baseUrl + path, content, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        logger.LogDebug("[TorrServe] POST {Url} → {Status} {Body}",
            _baseUrl + path, (int)resp.StatusCode, respJson.Length > 200 ? respJson[..200] + "…" : respJson);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize(respJson, resultInfo)!;
    }
}

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

public class TorrServeFile
{
    [JsonPropertyName("id")]     public int    Id     { get; set; }
    [JsonPropertyName("path")]   public string Path   { get; set; } = "";
    [JsonPropertyName("length")] public long   Length { get; set; }
}

// Internal — used only for parsing the v2 data JSON blob
internal class TorrServeData
{
    [JsonPropertyName("TorrServer")] public TorrServerSection? TorrServer { get; set; }
}

internal class TorrServerSection
{
    [JsonPropertyName("Files")] public TorrServeFile[]? Files { get; set; }
}

// Request DTOs — snake_case property names applied via TorrServeJsonContext options
internal record TorrentAddRequest(string Action, string Link, string Title, bool SaveToDb);
internal record TorrentActionRequest(string Action, string Hash);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TorrentAddRequest))]
[JsonSerializable(typeof(TorrentActionRequest))]
[JsonSerializable(typeof(TorrServeTorrent))]
[JsonSerializable(typeof(TorrServeFile))]
[JsonSerializable(typeof(TorrServeData))]
internal partial class TorrServeJsonContext : JsonSerializerContext { }
