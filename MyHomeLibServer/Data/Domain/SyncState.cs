namespace MyHomeLibServer.Data.Domain;

public class SyncState
{
    public long Id { get; set; }
    public string InpxFile { get; set; }
    public string Etag { get; set; }
    public long DurationMs { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsSynced { get; set; }
}