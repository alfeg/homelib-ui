using System.IO.Compression;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MyHomeLib.Library;
using Parquet;
using Parquet.Serialization;
using Spectre.Console;

namespace MyHomeListServer.Torrent;

public class LibraryIndexer
{
    private readonly DownloadManager _downloadManager;
    private readonly AppConfig _config;

    private static readonly Gate _gate = new Gate();

    public LibraryIndexer(DownloadManager downloadManager, IOptions<AppConfig> config)
    {
        _downloadManager = downloadManager;
        _config = config.Value;
    }

    public async Task IndexLibrary(string magnetUri)
    {
        var link = MagnetLink.Parse(magnetUri);
        var hash = link.InfoHashes.V1OrV2.ToHex();

        var reader = new InpxReader();
        var lib = new InpxLibrary();

        var inpxFiles = await _downloadManager.SearchFiles(new SearchRequest(hash, "*.inpx")
        {
            Link = link
        });

        if (inpxFiles.Names.Length != 1)
        {
            throw new AggregateException("Cannot index library there should only one *.inpx file");
        }

        var inpx = await _downloadManager.DownloadFile(new DownloadRequest(hash, inpxFiles.Names.Single())
        {
            Link = link
        });
        if (inpx.FullPath == null)
        {
            throw new ArgumentNullException("Cannot find downloaded file " + inpx.FullPath);
        }

        await AnsiConsole.Status().StartAsync("Reading inpx library", async ctx =>
        {
            var readLibraryAsync = reader.ReadLibraryAsync(inpx.FullPath, lib);
            var libraryEnumerable = readLibraryAsync.ToBlockingEnumerable().ToList();

            var dataFile = _config.DataFile(hash);
            if (File.Exists(dataFile))
            {
                File.Delete(dataFile);
            }

            AnsiConsole.MarkupLine($"[[{hash}]] Saving [green]{libraryEnumerable.Count}[/] books data to [bold]{dataFile}[/]");
            ctx.Status($"Saving");
            await ParquetSerializer.SerializeAsync(libraryEnumerable, dataFile, new ParquetSerializerOptions
            {
                CompressionMethod = CompressionMethod.Zstd,
                CompressionLevel = CompressionLevel.SmallestSize
            });
        });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Done[/]");
    }

    public IEnumerable<MonoTorrent.Torrent> ListLibraries()
    {
        foreach (var dataFile in Directory.EnumerateFiles(_config.TorrentsFolder(), "*.parquet"))
        {
            var torrentPath = _config.TorrentPath(InfoHash.FromHex(Path.GetFileNameWithoutExtension(dataFile)));
            if (!File.Exists(torrentPath))
            {
                continue;
            }

            var torrent = MonoTorrent.Torrent.Load(torrentPath);
            yield return torrent;
        }
    }

    public async Task<IList<BookItem>> ReadBooks(string hash)
    {
        var dataFile = _config.DataFile(hash);
        using var file = File.OpenRead(dataFile);
        var data = await ParquetSerializer.DeserializeAsync<BookItem>(file);
        return data;
    }
    
    public async IAsyncEnumerable<BookItem> SearchLibrary(string hash, string search)
    {
        // urlPrefix = urlPrefix.TrimEnd('/') + "/";

        IEnumerable<BookItem> SearchBooks(IList<BookItem> data)
        {
            foreach (var bookInfoDto in data)
            {
                if (bookInfoDto.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    bookInfoDto.Authors.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    yield return bookInfoDto;
                }
            }
        }

        using var _ = await _gate.Wait();

        var dataFile = _config.DataFile(hash);

        if (File.Exists(dataFile))
        {
            var books = await ReadBooks(hash);

            foreach (var p in SearchBooks(books))
            {
                yield return p;
            }

            yield break;
        }

        var inpxFiles = await _downloadManager.SearchFiles(new SearchRequest(hash, "*.inpx"));
        if (inpxFiles.Names.Length != 1)
        {
            throw new AggregateException("Cannot index library there should only one *.inpx file");
        }

        var inpx = await _downloadManager.DownloadFile(new DownloadRequest(hash, inpxFiles.Names.Single()));
        if (inpx.FullPath == null)
        {
            throw new ArgumentNullException("Cannot find downloaded file " + inpx.FullPath);
        }

        var reader = new InpxReader();
        var lib = new InpxLibrary();

        var readLibraryAsync = reader.ReadLibraryAsync(inpx.FullPath, lib);
        var libraryEnumerable = readLibraryAsync.ToBlockingEnumerable().ToList();

        await ParquetSerializer.SerializeAsync(libraryEnumerable, dataFile);

        foreach (var p in SearchBooks(libraryEnumerable))
        {
            yield return p;
        }
    }
}