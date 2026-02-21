using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MonoTorrent.Client;
using MonoTorrent;
using MyHomeLib.Web;
using MyHomeLib.Web.Components;
using MyHomeListServer.Torrent;

var builder = WebApplication.CreateBuilder(args);

// Serve _framework/blazor.web.js and other SDK static assets in all environments.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<LibraryConfig>(builder.Configuration.GetSection("Library"));
builder.Services.AddSingleton<LibraryService>();

// Torrent services (only DownloadManager + ClientEngine — no LibraryIndexer needed here)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("Torrent"));
builder.Services.AddSingleton<ClientEngine>(sp =>
{
    var config = sp.GetRequiredService<IOptions<AppConfig>>().Value;
    var endpoint = new IPEndPoint(IPAddress.Any, config.ListenPort);
    var settingsBuilder = new EngineSettingsBuilder
    {
        FastResumeMode       = FastResumeMode.BestEffort,
        CacheDirectory       = config.CacheDirectory,
        AutoSaveLoadDhtCache = true,   // persist DHT routing table across restarts
        AllowPortForwarding  = true,   // enable UPnP / NAT-PMP
        DhtEndPoint          = endpoint,
        ListenEndPoints      = new Dictionary<string, IPEndPoint> { ["ipv4"] = endpoint },
    };
    var factories = Factories.Default
        .WithStreamingPieceRequesterCreator(() => new PartialStreamingRequester(config));
    return new ClientEngine(settingsBuilder.ToSettings(), factories);
});
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddSingleton<DownloadQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadQueueService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Serve downloaded files
app.MapGet("/api/download/{jobId:guid}", async (Guid jobId, DownloadQueueService queue) =>
{
    var jobs = await queue.GetAllAsync();
    var job = jobs.FirstOrDefault(j => j.Id == jobId);
    if (job is null) return Results.NotFound();
    if (job.Status != DownloadStatus.Ready || job.FilePath is null) return Results.StatusCode(202);
    if (!File.Exists(job.FilePath)) return Results.NotFound("File not found on disk");

    var contentType = string.IsNullOrWhiteSpace(job.ContentType) ? "application/octet-stream" : job.ContentType;
    return Results.File(job.FilePath, contentType, job.DownloadName ?? Path.GetFileName(job.FilePath));
});

// Trigger startup tasks
_ = app.Services.GetRequiredService<LibraryService>().IndexTask;

app.Run();
