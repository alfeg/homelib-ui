using System.Text;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;

namespace MyHomeListServer.Torrent;

public static class TorrentsServiceExtension
{
    public static IServiceCollection AddTorrents(this IServiceCollection services, IConfiguration configuration)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        services.Configure<AppConfig>(configuration);
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<LibraryIndexer>();
        services.AddMemoryCache();
        services.AddSingleton<ClientEngine>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var settingsBuilder = new EngineSettingsBuilder
            {
                FastResumeMode = FastResumeMode.BestEffort,
                CacheDirectory = config.CacheDirectory,
            };
            var factories = Factories.Default
                .WithStreamingPieceRequesterCreator(() => new PartialStreamingRequester(config));

            return new ClientEngine(settingsBuilder.ToSettings(), factories);
        });

        return services;
    }
}