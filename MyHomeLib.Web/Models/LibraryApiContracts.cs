namespace MyHomeLib.Web.Models;

public sealed record LibraryBooksRequest(string MagnetUri, bool ForceReindex = false);

public sealed record LibraryDirectDownloadRequest(
    string MagnetUri,
    string ArchiveFile,
    string File,
    string Ext,
    string? Title,
    string? Authors);
