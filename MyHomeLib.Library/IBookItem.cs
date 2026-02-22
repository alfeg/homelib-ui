namespace MyHomeLib.Library;

public interface IBookItem
{
    int Id { get; }
    string? ArchiveFile { get; }
    string? File { get; }
    string? Ext { get;  }
}