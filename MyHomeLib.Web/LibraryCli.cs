using MyHomeLib.Web.Services.Search;

namespace MyHomeLib.Web;

/// <summary>
/// The <c>index</c> command: builds the search index and exits, without serving anything.
/// </summary>
/// <remarks>
/// Exists so index population can be scripted — a cron entry, a deploy step, a job run
/// before the service starts — rather than being a side effect of booting the web app.
/// The exit code distinguishes the outcomes a script actually needs to branch on, because
/// "index is fine but I could not refresh it" is neither success nor a hard failure.
/// </remarks>
public static class LibraryCli
{
    public const int ExitSuccess = 0;
    public const int ExitFailed = 1;

    /// <summary>Index unchanged and usable, but the collection could not be re-read.</summary>
    public const int ExitStale = 2;

    private const string IndexVerb = "index";

    public static bool IsIndexCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], IndexVerb, StringComparison.OrdinalIgnoreCase);

    public static bool IsHelpRequest(string[] args) =>
        args.Any(a => a is "--help" or "-h" or "-?" or "/?");

    public static void PrintHelp()
    {
        Console.WriteLine("""
            MyHomeLib

              (no arguments)      run the web application
              index               build the search index, then exit

            index options
              --force             rebuild even when the collection is unchanged
              --help              show this text

            index exit codes
              0   the index is current (rebuilt, or already up to date)
              1   indexing failed and no usable index is present
              2   the collection could not be read; an existing index is being served

            configuration comes from the usual sources, for example
              Library__MagnetUri   the library to index
              Library__InpxPath    a local .inpx, instead of fetching one
              Meili__Url           http://127.0.0.1:7700
            """);
    }

    public static bool WantsForce(string[] args) =>
        args.Any(a => a is "--force" or "-f");

    /// <summary>Runs one indexing pass and reports what happened.</summary>
    public static async Task<int> RunIndexAsync(IServiceProvider services, CancellationToken ct)
    {
        var indexer = services.GetRequiredService<LibraryIndexerService>();
        var state = services.GetRequiredService<IndexState>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MyHomeLib.Index");

        // Indexing a large collection is minutes of silence otherwise, which reads as a hang
        // in a build log.
        using var progress = new Timer(_ =>
        {
            var snapshot = state.Snapshot();
            if (snapshot.Phase is not (nameof(IndexPhase.Ready)) and not (nameof(IndexPhase.Failed)))
                logger.LogInformation("[Index] {Phase} {Percent}% — {Message}",
                    snapshot.Phase, snapshot.Percent, snapshot.Message);
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        try
        {
            var outcome = await indexer.IndexAsync(ct);
            var snapshot = state.Snapshot();

            switch (outcome)
            {
                case IndexOutcome.Rebuilt:
                    logger.LogInformation("[Index] rebuilt — {Total:N0} books in {Collection}",
                        snapshot.TotalBooks, snapshot.CollectionTitle);
                    return ExitSuccess;

                case IndexOutcome.UpToDate:
                    logger.LogInformation("[Index] already up to date — {Total:N0} books",
                        snapshot.TotalBooks);
                    return ExitSuccess;

                default:
                    logger.LogWarning("[Index] collection unreadable — the existing index of " +
                        "{Total:N0} books is unchanged and may be stale", snapshot.TotalBooks);
                    return ExitStale;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("[Index] cancelled");
            return ExitFailed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Index] failed");
            return ExitFailed;
        }
    }
}
