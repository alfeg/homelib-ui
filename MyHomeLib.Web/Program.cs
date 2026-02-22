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

// Derive torrent CacheDirectory from Library:DownloadsDirectory so all torrent data stays
// in the configured library folder. An explicit Torrent:CacheDirectory in config overrides this.
builder.Services.PostConfigure<AppConfig>(opts =>
{
    if (opts.CacheDirectory == "./cache")
    {
        var downloadsDir = builder.Configuration["Library:DownloadsDirectory"];
        if (!string.IsNullOrWhiteSpace(downloadsDir))
            opts.CacheDirectory = downloadsDir;
    }
});

builder.Services.AddSingleton<ClientEngine>(sp =>
{
    var config = sp.GetRequiredService<IOptions<AppConfig>>().Value;
    var endpoint = new IPEndPoint(IPAddress.Any, config.ListenPort);
    // Engine internal cache (DHT nodes, fast-resume) → <downloads>/.cache/
    var settingsBuilder = new EngineSettingsBuilder
    {
        FastResumeMode       = FastResumeMode.BestEffort,
        CacheDirectory       = config.CacheDirectory(), // extension method → base/.cache
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

// First Ctrl+C → graceful shutdown (default .NET behaviour, cancel handled by host).
// Second Ctrl+C → force-kill immediately so unreachable trackers can't stall the process.
var ctrlCCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    if (++ctrlCCount > 1)
    {
        Console.Error.WriteLine("Force exit.");
        Environment.Exit(1);
    }
    // First press: let the host lifetime handle it gracefully.
    e.Cancel = true;
    app.Lifetime.StopApplication();
};

app.Run();
