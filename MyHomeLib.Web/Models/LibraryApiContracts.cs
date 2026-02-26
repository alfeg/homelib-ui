namespace MyHomeLib.Web.Models;

public sealed record LibraryBooksRequest(string MagnetUri);

public sealed record LibraryDirectDownloadRequest(
    string MagnetUri,
    string ArchiveFile,
    string File,
    string Ext,
    string? Title,
    string? Authors);
