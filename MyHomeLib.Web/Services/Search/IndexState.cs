namespace MyHomeLib.Web.Services.Search;

public enum IndexPhase
{
    Pending,
    WaitingForSearchEngine,
    Downloading,
    Parsing,
    Indexing,
    Ready,
    Failed,
}

/// <summary>
/// Live indexing status, shared by every surface. First boot takes minutes, so OPDS,
/// the thin pages and the JSON API all read this to answer "not ready yet" honestly
/// instead of returning an empty library or an error.
/// </summary>
public class IndexState
{
    private readonly Lock _gate = new();

    public IndexPhase Phase { get; private set; } = IndexPhase.Pending;
    public int Percent { get; private set; }
    public string Message { get; private set; } = "Starting up";
    public string? Error { get; private set; }
    public long TotalBooks { get; private set; }
    public string? CollectionTitle { get; private set; }
    public string? CollectionVersion { get; private set; }
    public DateTimeOffset? IndexedAt { get; private set; }

    public bool IsReady => Phase == IndexPhase.Ready;

    public void Set(IndexPhase phase, string message, int percent = 0)
    {
        lock (_gate)
        {
            Phase = phase;
            Message = message;
            Percent = Math.Clamp(percent, 0, 100);
            if (phase != IndexPhase.Failed) Error = null;
        }
    }

    public void Progress(int percent, string message)
    {
        lock (_gate)
        {
            Percent = Math.Clamp(percent, 0, 100);
            Message = message;
        }
    }

    public void Ready(long totalBooks, string? collectionTitle, string? collectionVersion, DateTimeOffset indexedAt)
    {
        lock (_gate)
        {
            Phase = IndexPhase.Ready;
            Percent = 100;
            Message = "Ready";
            Error = null;
            TotalBooks = totalBooks;
            CollectionTitle = collectionTitle;
            CollectionVersion = collectionVersion;
            IndexedAt = indexedAt;
        }
    }

    public void Fail(string error)
    {
        lock (_gate)
        {
            Phase = IndexPhase.Failed;
            Message = "Indexing failed";
            Error = error;
        }
    }

    public IndexStatusSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new IndexStatusSnapshot(
                Phase.ToString(), Percent, Message, Error, TotalBooks,
                CollectionTitle, CollectionVersion, IndexedAt, IsReady);
        }
    }
}

public record IndexStatusSnapshot(
    string Phase,
    int Percent,
    string Message,
    string? Error,
    long TotalBooks,
    string? CollectionTitle,
    string? CollectionVersion,
    DateTimeOffset? IndexedAt,
    bool Ready);
