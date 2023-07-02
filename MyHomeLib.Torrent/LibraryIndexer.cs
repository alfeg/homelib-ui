using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MonoTorrent.Client;
using MyHomeLib.Library;
using Parquet.Serialization;

namespace MyHomeListServer.Torrent;

public static class TorrentsServiceExtension
{
    public static void AddTorrents(this IServiceCollection services, IConfiguration configuration)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
       services.Configure<AppConfig>(configuration);
       services.AddSingleton<DownloadManager>();
       services.AddSingleton<LibraryIndexer>();
       services.AddMemoryCache();
       services.AddSingleton<ClientEngine>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var settingsBuilder = new EngineSettingsBuilder()
            {
                FastResumeMode = FastResumeMode.BestEffort,
                CacheDirectory = config.CacheDirectory,
            };
    
            var engine = new ClientEngine(settingsBuilder.ToSettings());
            return engine;
        });
    }
}

public class LibraryIndexer
{
    private readonly DownloadManager _downloadManager;
    private readonly IMemoryCache _memoryCache;
    private readonly AppConfig _config;

    private static readonly Gate _gate = new Gate(); 
    
    public LibraryIndexer(DownloadManager downloadManager, IOptions<AppConfig> config, IMemoryCache memoryCache)
    {
        _downloadManager = downloadManager;
        _memoryCache = memoryCache;
        _config = config.Value;
    }

    public async IAsyncEnumerable<BookInfoDto> SearchLibrary(string hash, string search, string urlPrefix)
    {
        urlPrefix = urlPrefix.TrimEnd('/') + "/";
        
        IEnumerable<BookInfoDto> SearchBooks(IList<BookInfoDto> data)
        {
            foreach (var bookInfoDto in data)
            {
                if (bookInfoDto.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || bookInfoDto.Author.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    // bookInfoDto.UrlPart = urlPrefix + bookInfoDto.UrlPart;
                    yield return bookInfoDto with
                    {
                        UrlPart = urlPrefix + bookInfoDto.UrlPart
                    };
                }
            }
        }

        using var _ = await _gate.Wait();
        
        var dataFile = _config.DataFile(hash);

        if (File.Exists(dataFile))
        {
            var books = await _memoryCache.GetOrCreateAsync($"books:{hash}", async entry =>
            {
                using var file = File.OpenRead(dataFile);
                var data = await ParquetSerializer.DeserializeAsync<BookInfoDto>(file);
                entry.SetSlidingExpiration(TimeSpan.FromMinutes(10));
                return data;
            });
            
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
        var libraryEnumerable = readLibraryAsync.ToBlockingEnumerable()
            .Select(book => new BookInfoDto
                {
                    Id = book.Id,
                    Title = book.Title,
                    Author = book.Authors,
                    Series = book.Series,
                    UrlPart = $"get/{hash}/{book.ArchiveFile}/{book.File}.{book.Ext}"
                }).ToList();

        await ParquetSerializer.SerializeAsync(libraryEnumerable, dataFile);
        
        foreach (var p in SearchBooks(libraryEnumerable))
        {
            yield return p;
        }
    }
}