namespace MyHomeLib.Web.Models;

/// <remarks>
/// No MagnetUri: the library is fixed by <see cref="LibraryConfig"/>. Clients that still
/// send one (the unmigrated SPA) simply have it ignored during binding.
/// </remarks>
public sealed record LibraryDirectDownloadRequest(
    string ArchiveFile,
    string File,
    string Ext,
    string? Title,
    string? Authors);
