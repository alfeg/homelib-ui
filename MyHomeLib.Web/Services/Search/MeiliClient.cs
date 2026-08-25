using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;

namespace MyHomeLib.Web.Services.Search;

/// <summary>
/// Thin client for the Meilisearch HTTP API. Only the endpoints this app needs.
/// </summary>
public class MeiliClient(HttpClient http, IOptions<MeiliConfig> config, ILogger<MeiliClient> logger)
{
    private readonly MeiliConfig _config = config.Value;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string IndexUid => _config.IndexUid;

    // ── Health ───────────────────────────────────────────────────────────────

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync("/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogDebug("[Meili] health check failed: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>Blocks until Meilisearch answers /health, or the timeout expires.</summary>
    public async Task<bool> WaitUntilHealthyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var attempt = 0;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await IsHealthyAsync(ct)) return true;

            attempt++;
            logger.LogInformation("[Meili] not ready yet (attempt {Attempt}), retrying…", attempt);
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), ct);
        }

        return false;
    }

    // ── Index lifecycle ──────────────────────────────────────────────────────

    public async Task EnsureIndexAsync(string uid, string primaryKey, CancellationToken ct = default)
    {
        // Authorized like every other call: an unauthenticated probe returns 401 against a
        // keyed instance, which reads as "no such index" and leads straight into a
        // create that fails with index_already_exists.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/indexes/{uid}");
        Authorize(request);

        using var resp = await http.SendAsync(request, ct);
        if (resp.IsSuccessStatusCode) return;

        if (resp.StatusCode is not HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Meilisearch {(int)resp.StatusCode} while checking index {uid}: {body}");
        }

        logger.LogInformation("[Meili] creating index {Uid} (primary key {Key})", uid, primaryKey);

        try
        {
            var task = await PostAsync<object, MeiliTask>("/indexes", new { uid, primaryKey }, ct);
            await WaitForTaskAsync(task.TaskUid, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Another instance won the race; the index existing is the desired end state.
            logger.LogInformation("[Meili] index {Uid} was created concurrently", uid);
        }
    }

    public async Task ApplySettingsAsync(string uid, MeiliIndexSettings settings, CancellationToken ct = default)
    {
        var task = await SendAsync<MeiliIndexSettings, MeiliTask>(
            HttpMethod.Patch, $"/indexes/{uid}/settings", settings, ct);
        await WaitForTaskAsync(task.TaskUid, ct);
    }

    public async Task DeleteAllDocumentsAsync(string uid, CancellationToken ct = default)
    {
        var task = await SendAsync<object?, MeiliTask>(
            HttpMethod.Delete, $"/indexes/{uid}/documents", null, ct);
        await WaitForTaskAsync(task.TaskUid, ct);
    }

    /// <summary>Deletes every document matching a Meilisearch filter expression.</summary>
    public async Task DeleteDocumentsByFilterAsync(string uid, string filter, CancellationToken ct = default)
    {
        var task = await PostAsync<object, MeiliTask>(
            $"/indexes/{uid}/documents/delete", new { filter }, ct);
        await WaitForTaskAsync(task.TaskUid, ct);
    }

    // ── Documents ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes a batch as NDJSON. Returns the task id; the caller decides whether to await it.
    /// NDJSON avoids building one giant JSON array in memory for a 10k-document batch.
    /// </summary>
    public async Task<int> AddDocumentsAsync<T>(string uid, IReadOnlyCollection<T> documents, CancellationToken ct = default)
    {
        var buffer = new MemoryStream(documents.Count * 200);
        await using (var writer = new StreamWriter(buffer, new UTF8Encoding(false), leaveOpen: true))
        {
            foreach (var document in documents)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(document, Json));
            }
        }

        buffer.Position = 0;
        using var content = new StreamContent(buffer);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-ndjson");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/indexes/{uid}/documents") { Content = content };
        Authorize(request);

        using var resp = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(resp, ct);

        var task = await ReadAsync<MeiliTask>(resp, ct);
        return task.TaskUid;
    }

    public async Task<BookDocument?> GetDocumentAsync(string uid, string documentId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/indexes/{uid}/documents/{Uri.EscapeDataString(documentId)}");
        Authorize(request);

        using var resp = await http.SendAsync(request, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(resp, ct);
        return await ReadAsync<BookDocument>(resp, ct);
    }

    /// <summary>Number of documents currently in the index.</summary>
    public async Task<long> CountDocumentsAsync(string uid, CancellationToken ct = default)
    {
        var stats = await GetAsync<MeiliIndexStats>($"/indexes/{uid}/stats", ct);
        return stats.NumberOfDocuments;
    }

    public Task<MeiliSearchResponse> SearchAsync(string uid, MeiliSearchRequest request, CancellationToken ct = default) =>
        PostAsync<MeiliSearchRequest, MeiliSearchResponse>($"/indexes/{uid}/search", request, ct);

    // ── Index state record ───────────────────────────────────────────────────

    public async Task<IndexStateDocument?> GetIndexStateAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/indexes/{_config.MetaIndexUid}/documents/state");
        Authorize(request);

        using var resp = await http.SendAsync(request, ct);
        if (resp.StatusCode is HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) return null;

        return await ReadAsync<IndexStateDocument>(resp, ct);
    }

    public async Task SaveIndexStateAsync(IndexStateDocument state, CancellationToken ct = default)
    {
        await EnsureIndexAsync(_config.MetaIndexUid, "uid", ct);
        var taskUid = await AddDocumentsAsync(_config.MetaIndexUid, new[] { state }, ct);
        await WaitForTaskAsync(taskUid, ct);
    }

    // ── Tasks ────────────────────────────────────────────────────────────────

    /// <summary>Polls a task to completion. Throws when Meilisearch reports failure.</summary>
    public async Task WaitForTaskAsync(int taskUid, CancellationToken ct = default)
    {
        var delay = TimeSpan.FromMilliseconds(50);

        while (!ct.IsCancellationRequested)
        {
            var task = await GetAsync<MeiliTask>($"/tasks/{taskUid}", ct);

            switch (task.Status)
            {
                case "succeeded":
                    return;
                case "failed":
                case "canceled":
                    throw new InvalidOperationException(
                        $"Meilisearch task {taskUid} ({task.Type}) {task.Status}: {task.Error?.Message}");
            }

            await Task.Delay(delay, ct);
            // Bulk indexing tasks take seconds; back off so a long task is not polled hundreds of times.
            if (delay < TimeSpan.FromSeconds(2)) delay *= 2;
        }

        ct.ThrowIfCancellationRequested();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private void Authorize(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
    }

    private Task<TRes> GetAsync<TRes>(string path, CancellationToken ct) =>
        SendAsync<object?, TRes>(HttpMethod.Get, path, null, ct);

    private Task<TRes> PostAsync<TReq, TRes>(string path, TReq body, CancellationToken ct) =>
        SendAsync<TReq, TRes>(HttpMethod.Post, path, body, ct);

    private async Task<TRes> SendAsync<TReq, TRes>(HttpMethod method, string path, TReq? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        Authorize(request);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        using var resp = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(resp, ct);
        return await ReadAsync<TRes>(resp, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, Json)
               ?? throw new InvalidOperationException($"Meilisearch returned an empty {typeof(T).Name}.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        logger.LogError("[Meili] {Status} {Path} → {Body}",
            (int)resp.StatusCode, resp.RequestMessage?.RequestUri?.PathAndQuery, body);

        throw new HttpRequestException($"Meilisearch {(int)resp.StatusCode}: {body}");
    }
}
