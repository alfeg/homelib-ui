using Fb2.Document;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MyHomeLib.Files.Core;

namespace MyHomeLib.Files.Torrents;

public record DownloadRequest(string Library, string Archive, string? Book = null)
{
    public override string ToString()
    {
        return $"[{Library}] {Name}";
    }

    public string Name => $"{Archive} => {Book}";
}

//
// public class TorrentService
// {
//     private readonly IOptions<AppConfig> _config;
//     private readonly ClientEngine _clientEngine;
//
//     public TorrentService(IOptions<AppConfig> config, ClientEngine clientEngine)
//     {
//         _config = config;
//         _clientEngine = clientEngine;
//     }
//
//     public async Task<DownloadResponse> DownloadFile(DownloadRequest request, CancellationToken cancellationToken)
//     {
//         var libraryInfoHash = request.Library;
//         var archive = request.Archive;
//         var book = request.Book;
//
//         var link = new MagnetLink(InfoHash.FromHex(libraryInfoHash));
//
//         var appConfig = _config.Value;
//
//         var libTorrent = await _clientEngine.DownloadTorrentFileAsync(link, appConfig);
//
//         TorrentManager st = _clientEngine.Torrents.FirstOrDefault(t => t.InfoHash.ToHex() == libraryInfoHash);
//         if (st == null)
//         {
//             st = await _clientEngine.AddStreamingAsync(libTorrent, appConfig.TorrentsFolder(libraryInfoHash));
//         }
//
//         st.TrackerManager.AddTrackerAsync(new Uri("http://192.168.3.26:9000/announce"));
//
//         await st.TrackerManager.AnnounceAsync(cancellationToken);
//         //st.AddPeerAsync(new MonoTorrent.Client.Peer())
//
//         var fileToDownload = st.Files.First(f => f.Path == archive);
//         await st.SetDownloadOnly(fileToDownload.Path);
//         await st.StartAsync();
//
//         await using var stream = await st.StreamProvider.CreateStreamAsync(fileToDownload, false, cancellationToken);
//
//         var bookStream = new MemoryStream();
//         if (book == null)
//         {
//             await stream.CopyToAsync(bookStream, cancellationToken);
//             await st.StopAsync();
//             return new DownloadResponse(bookStream, string.Empty, string.Empty);
//         }
//
//         stream.Seek(0, SeekOrigin.Begin);
//
//         var buffer = new byte[100_000];
//         await stream.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
//
//         stream.Seek(0, SeekOrigin.Begin);
//
//         using var zip = new ZipFile(stream);
//         var entry = zip.GetEntry(book);
//
//         stream.Seek(entry.Offset, SeekOrigin.Begin);
//
//         var resultBuffer = new byte[entry.CompressedSize];
//         await stream.ReadAtLeastAsync(resultBuffer, resultBuffer.Length, false, cancellationToken);
//         var entryStream = zip.GetInputStream(entry);
//
//         await entryStream.CopyToAsync(bookStream, cancellationToken);
//
//         await st.StopAsync();
//         await _clientEngine.RemoveAsync(libTorrent);
//
//         bookStream.Seek(0, SeekOrigin.Begin);
//         if (book.EndsWith(".fb2"))
//         {
//             var fb2 = new Fb2Document();
//             await fb2.LoadAsync(bookStream);
//             bookStream.Seek(0, SeekOrigin.Begin);
//             var title = fb2.Title?.Content.FirstOrDefault(c => c.Name == "book-title")?.ToString();
//
//             return new DownloadResponse(bookStream, "application/fb2",
//                 (title ?? Path.GetFileNameWithoutExtension(book)) + ".fb2");
//         }
//
//         return new DownloadResponse(bookStream, "application/fb2", book);
//     }
// }