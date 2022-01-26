using Microsoft.Extensions.Options;

namespace MyHomeLibServer.Data;

public class LibraryInitBgService : BackgroundService
{
    private readonly ImportDataService importDataService;
    private readonly LibraryAccessor libraryAccessor;
    private readonly IOptions<LibraryConfig> options;

    public LibraryInitBgService(ImportDataService importDataService, LibraryAccessor libraryAccessor, IOptions<LibraryConfig> options)
    {
        this.importDataService = importDataService;
        this.libraryAccessor = libraryAccessor;
        this.options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isSyncRequired = await importDataService.IsSyncRequired();
        var indexFile = options.Value.CatalogIndexFile;
        if(indexFile == null)
        {
            return;
        }

        libraryAccessor.Library.LibraryFolder = Path.GetDirectoryName(indexFile)!;
        if (isSyncRequired)
        {
            await importDataService.SyncDataAsync(stoppingToken);
        }
    }
}
