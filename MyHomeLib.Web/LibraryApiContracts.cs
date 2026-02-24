namespace MyHomeLib.Web;

public sealed record LibraryBooksRequest(string MagnetUri, bool ForceReindex = false);

public sealed record LibraryBooksResponse(string Hash, LibraryBooksMetadata Metadata, IReadOnlyList<LibraryBookItem> Books);

public sealed record LibraryBooksMetadata(string? Description, string? Version, int TotalBooks);

public sealed record LibraryBookItem(
    int Id,
    string? Title,
    string? Authors,
    string? Series,
    string? SeriesNo,
    string? Lang,
    string? File,
    string? Ext,
    string? ArchiveFile);

public sealed record LibraryDirectDownloadRequest(
    string MagnetUri,
    string ArchiveFile,
    string File,
    string Ext,
    string? Title,
    string? Authors);
