using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using MyHomeLib.Web.Models;

namespace MyHomeLib.Web.Services.Search;

/// <summary>What an indexing run actually did, so a script can tell the cases apart.</summary>
public enum IndexOutcome
{
    /// <summary>The collection changed (or a rebuild was forced) and documents were written.</summary>
    Rebuilt,

    /// <summary>The collection was already indexed and identical; nothing to do.</summary>
    UpToDate,

    /// <summary>
    /// The collection could not be read, but a previously built index was present and is
    /// being served. The index is usable but may be stale.
    /// </summary>
    ServedExisting,
}

/// <summary>
/// Builds the Meilisearch index at startup: acquire the INPX, parse it, bulk load it.
/// Skips the whole thing when the stored signature matches the INPX on hand, so a
/// container restart is seconds rather than minutes.
/// </summary>
public class LibraryIndexerService(
    IServiceScopeFactory scopeFactory,
    MeiliClient meili,
    IndexState state,
    IOptions<LibraryConfig> libraryConfig,
    ILogger<LibraryIndexerService> logger) : BackgroundService
{
    private readonly LibraryConfig _config = libraryConfig.Value;

    /// <summary>
    /// Bump whenever <see cref="BookDocument"/> gains, loses or changes a field, so existing
    /// documents get rewritten even though the collection's content is unchanged.
    /// </summary>
    private const int DocumentSchemaVersion = 4;

    /// <summary>Attempts allowed when the downloaded INPX turns out not to be readable.</summary>
    private const int InpxAcquireAttempts = 5;

    private static readonly TimeSpan InpxRetryDelay = TimeSpan.FromSeconds(5);

    private static readonly MeiliIndexSettings BookIndexSettings = new()
    {
        // Order matters: Meilisearch ranks matches by the position of the attribute in this list.
        // These are the ё-folded copies; the display fields are deliberately not searchable,
        // which also keeps the index from building word structures for the same text twice.
        SearchableAttributes = ["titleSearch", "authorsSearch", "seriesSearch"],
        FilterableAttributes = ["genres", "lang", "year", "ext", "authorList", "archive", "series", "deleted"],
        SortableAttributes = ["year", "id", "title", "seriesIndex", "series"],
        DisplayedAttributes = ["*"],
        // A real collection carries ~278 genre codes; the default cap of 100 would truncate the sidebar.
        Faceting = new MeiliFaceting
        {
            MaxValuesPerFacet = 400,
            SortFacetValuesBy = new Dictionary<string, string> { ["*"] = "count" },
        },
        // Default maxTotalHits (1000) would make deep OPDS paging hit an invisible wall.
        Pagination = new MeiliPaginationSettings { MaxTotalHits = 100_000 },
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await IndexAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("[Index] startup indexing cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Index] startup indexing failed");
            state.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Runs one indexing pass. Public so the <c>index</c> command can drive it without a web
    /// host; the hosted service is just this method on a background thread.
    /// </summary>
    public async Task<IndexOutcome> IndexAsync(CancellationToken ct)
    {
        state.Set(IndexPhase.WaitingForSearchEngine, "Waiting for the search engine");

        if (!await meili.WaitUntilHealthyAsync(TimeSpan.FromMinutes(5), ct))
            throw new InvalidOperationException("Meilisearch did not become healthy within 5 minutes.");

        // Read before touching the INPX: an index that is already built is what lets a
        // failed read degrade instead of taking the whole server down.
        var stored = await meili.GetIndexStateAsync(ct);

        // Acquiring and parsing are one retried step. A cold torrent can serve a file that
        // is the right size and shape but wrong content, and the damage only shows up when
        // something deep inside fails to inflate — reported as "unsupported compression
        // method" or "a local file header is corrupt", neither of which sounds like a
        // partial download. Parsing is the only honest proof the bytes are intact, and it
        // has to happen anyway to fingerprint the collection, so it costs nothing extra.
        byte[] inpx;
        InpxLibrary metadata;
        InpxSignatureResult fingerprint;

        for (var attempt = 1; ; attempt++)
        {
            inpx = await AcquireInpxAsync(ct);

            state.Set(IndexPhase.Parsing, "Reading the collection");
            try
            {
                using var metadataStream = new MemoryStream(inpx, writable: false);
                metadata = InpxReader.ReadMetadata(metadataStream);

                // Content fingerprint, not a byte-length one: the same collection reached
                // through a different magnet or re-zipped elsewhere must not force a rebuild.
                using var signatureStream = new MemoryStream(inpx, writable: false);
                // Fingerprint the same set that will be indexed, or the counts disagree.
                fingerprint = InpxSignature.Compute(signatureStream, _config.IncludeDeleted);
                break;
            }
            catch (InvalidDataException ex)
            {
                // A local file is whatever it is; fetching it again will not change anything.
                if (attempt >= InpxAcquireAttempts || !string.IsNullOrWhiteSpace(_config.InpxPath))
                {
                    // The INPX is only read to decide whether a rebuild is needed. If one was
                    // built earlier it is still perfectly good, and refusing to serve it would
                    // turn a transient download problem into an outage. Serve what we have and
                    // say so; the next restart will try the collection again.
                    if (stored is { Total: > 0 } && stored.SchemaVersion == DocumentSchemaVersion)
                    {
                        logger.LogError(ex,
                            "[Index] collection index unreadable after {Attempts} attempts — serving the " +
                            "existing index of {Total:N0} books built at {IndexedAt}",
                            attempt, stored.Total, stored.IndexedAt);

                        state.Ready(stored.Total, stored.CollectionTitle, stored.CollectionVersion,
                            DateTimeOffset.TryParse(stored.IndexedAt, out var previous) ? previous : DateTimeOffset.UtcNow);
                        return IndexOutcome.ServedExisting;
                    }

                    throw new InvalidDataException(
                        $"The collection index could not be read: {ex.Message} " +
                        "This usually means an incomplete download rather than a damaged collection.", ex);
                }

                logger.LogWarning("[Index] collection index unreadable on attempt {Attempt} ({Message}); fetching again",
                    attempt, ex.Message);

                state.Set(IndexPhase.Downloading, "Collection index was incomplete — fetching it again");
                await Task.Delay(InpxRetryDelay, ct);
            }
        }

        var collectionTitle = FirstLine(metadata.Description) ?? _config.Title;
        var signature = fingerprint.Value;
        logger.LogInformation("[Index] collection fingerprint {Signature} ({Count:N0} books)",
            signature, fingerprint.BookCount);

        // Settings are applied on every start, not just on a rebuild: changing a filterable
        // or sortable attribute must not require re-pushing 586k unchanged documents.
        await meili.EnsureIndexAsync(meili.IndexUid, "uid", ct);
        await meili.ApplySettingsAsync(meili.IndexUid, BookIndexSettings, ct);

        if (!_config.ForceReindex
            && stored?.Signature == signature
            && stored.SchemaVersion == DocumentSchemaVersion)
        {
            logger.LogInformation("[Index] fingerprint unchanged — {Total:N0} books already indexed", stored.Total);
            state.Ready(stored.Total, stored.CollectionTitle, stored.CollectionVersion,
                DateTimeOffset.TryParse(stored.IndexedAt, out var at) ? at : DateTimeOffset.UtcNow);
            return IndexOutcome.UpToDate;
        }

        // Work out the smallest change that gets the index current. A brand-new index, or an
        // explicit ForceReindex, means everything; otherwise only archives whose fingerprint
        // moved, which for an append-only collection is the newly added ones.
        var schemaChanged = stored is not null && stored.SchemaVersion != DocumentSchemaVersion;
        if (schemaChanged)
        {
            logger.LogInformation("[Index] document schema {Old} → {New}: full rebuild required",
                stored!.SchemaVersion, DocumentSchemaVersion);
        }

        var storedArchives = _config.ForceReindex || schemaChanged ? [] : stored?.Archives ?? [];
        var isFirstBuild = storedArchives.Count == 0;

        var changed = fingerprint.Archives
            .Where(a => storedArchives.GetValueOrDefault(a.Key) != a.Value)
            .Select(a => a.Key)
            .ToHashSet();

        var removed = storedArchives.Keys
            .Where(name => !fingerprint.Archives.ContainsKey(name))
            .ToList();

        logger.LogInformation(
            "[Index] {Changed} of {Total} archives changed, {Removed} removed ({Mode})",
            changed.Count, fingerprint.Archives.Count, removed.Count,
            isFirstBuild ? "first build" : "incremental");

        if (isFirstBuild)
        {
            // Nothing trustworthy to diff against, so start from a clean index.
            await meili.DeleteAllDocumentsAsync(meili.IndexUid, ct);
        }
        else
        {
            // Clear every archive that is being replaced, not just the ones that vanished.
            // Upserting a changed archive's current books would leave behind any book that
            // was deleted from it or moved elsewhere, and those ghosts accumulate on each
            // update. One batched delete keeps this to a single Meilisearch task.
            var toClear = changed.Concat(removed).Distinct().ToList();

            if (toClear.Count > 0)
            {
                var list = string.Join(", ", toClear.Select(a => $"'{Escape(a)}'"));
                logger.LogInformation("[Index] clearing {Count} archives before repopulating", toClear.Count);
                await meili.DeleteDocumentsByFilterAsync(meili.IndexUid, $"archive IN [{list}]", ct);
            }
        }

        var indexed = await IndexBooksAsync(inpx, changed, indexEverything: isFirstBuild, ct);

        // Documents are upserted by uid, so the collection total is whatever the engine now
        // holds rather than what this run happened to push.
        var total = isFirstBuild ? indexed : await meili.CountDocumentsAsync(meili.IndexUid, ct);

        var indexedAt = DateTimeOffset.UtcNow;
        await meili.SaveIndexStateAsync(new IndexStateDocument
        {
            Signature = signature,
            Total = total,
            CollectionTitle = collectionTitle,
            CollectionVersion = metadata.Version,
            IndexedAt = indexedAt.ToString("O"),
            SchemaVersion = DocumentSchemaVersion,
            Archives = fingerprint.Archives.ToDictionary(a => a.Key, a => a.Value),
        }, ct);

        logger.LogInformation("[Index] done — {Pushed:N0} pushed, {Total:N0} in the collection", indexed, total);
        state.Ready(total, collectionTitle, metadata.Version, indexedAt);
        return IndexOutcome.Rebuilt;
    }

    /// <summary>Meilisearch filter literals are single-quoted.</summary>
    private static string Escape(string value) => value.Replace("'", "\'");

    // ── INPX acquisition ─────────────────────────────────────────────────────

    private async Task<byte[]> AcquireInpxAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_config.InpxPath))
        {
            state.Set(IndexPhase.Downloading, $"Reading {Path.GetFileName(_config.InpxPath)}");
            logger.LogInformation("[Index] reading local INPX {Path}", _config.InpxPath);

            if (!File.Exists(_config.InpxPath))
                throw new FileNotFoundException($"Library:InpxPath points at a missing file: {_config.InpxPath}");

            return await File.ReadAllBytesAsync(_config.InpxPath, ct);
        }

        if (string.IsNullOrWhiteSpace(_config.MagnetUri))
            throw new InvalidOperationException(
                "No library configured. Set Library__MagnetUri (or Library__InpxPath for local development).");

        state.Set(IndexPhase.Downloading, "Downloading the collection index via TorrServe");
        logger.LogInformation("[Index] fetching INPX via TorrServe");

        // DownloadManager is transient and pulls a scoped HttpClient, so take a scope.
        using var scope = scopeFactory.CreateScope();
        var downloads = scope.ServiceProvider.GetRequiredService<DownloadManager>();

        var (data, fileName) = await downloads.GetInpxFileAsync(_config.MagnetUri, ct);
        logger.LogInformation("[Index] got {FileName} ({Mb} MB)", fileName, data.Length / 1024 / 1024);
        return data;
    }

    // ── Bulk load ────────────────────────────────────────────────────────────

    private async Task<long> IndexBooksAsync(
        byte[] inpx, IReadOnlySet<string> archives, bool indexEverything, CancellationToken ct)
    {
        if (!indexEverything && archives.Count == 0)
        {
            logger.LogInformation("[Index] no archive changed — nothing to push");
            return 0;
        }

        state.Set(IndexPhase.Indexing, "Indexing books", 0);

        using var stream = new MemoryStream(inpx, writable: false);
        var library = new InpxLibrary();
        var progress = new Progress<InpxProgress>(p =>
            state.Progress(p.Percent, $"Indexing books — {p.BooksRead:N0} scanned"));

        var batch = new List<BookDocument>(_config.IndexBatchSize);
        long indexed = 0;

        foreach (var book in InpxReader.ReadLibrary(stream, library, _config.IncludeDeleted, progress))
        {
            ct.ThrowIfCancellationRequested();

            if (!indexEverything && !archives.Contains(book.ArchiveFile ?? "")) continue;

            batch.Add(BookDocument.From(book));

            if (batch.Count < _config.IndexBatchSize) continue;

            indexed += await FlushAsync(batch, ct);
            batch.Clear();
        }

        if (batch.Count > 0) indexed += await FlushAsync(batch, ct);

        logger.LogInformation("[Index] {Read:N0} rows read, {Pushed:N0} pushed ({Skipped:N0} skipped as deleted)",
            library.BooksRead, indexed, library.BooksRead - library.BooksAdded);

        return indexed;
    }

    /// <summary>
    /// Awaits each batch rather than queueing them all: Meilisearch buffers pending tasks
    /// in memory, and this branch targets hosts where that is the scarce resource.
    /// </summary>
    private async Task<int> FlushAsync(List<BookDocument> batch, CancellationToken ct)
    {
        var taskUid = await meili.AddDocumentsAsync(meili.IndexUid, batch, ct);
        await meili.WaitForTaskAsync(taskUid, ct);
        logger.LogDebug("[Index] flushed {Count} documents (task {Task})", batch.Count, taskUid);
        return batch.Count;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? FirstLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
}
