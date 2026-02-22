using System.Text;
using MyHomeLib.Web;
using MyHomeLib.Web.Components;
using MyHomeListServer.Torrent;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.Configure<LibraryConfig>(builder.Configuration.GetSection("Library"));
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("Torrent"));

// TorrServe services
var torrServeUrl = builder.Configuration["Torrent:TorrServeUrl"]
    ?? throw new InvalidOperationException("Torrent:TorrServeUrl is required.");

builder.Services.AddSingleton<TorrServeClient>(sp =>
    new TorrServeClient(
        new HttpClient(),
        torrServeUrl,
        sp.GetRequiredService<ILogger<TorrServeClient>>()));

builder.Services.AddSingleton<DownloadManager>(sp =>
    new DownloadManager(
        sp.GetRequiredService<TorrServeClient>(),
        new HttpClient(),
        sp.GetRequiredService<ILogger<DownloadManager>>()));

// Library initialisation runs in the background (downloads INPX, builds DuckDB index)
builder.Services.AddSingleton<LibraryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryService>());

builder.Services.AddSingleton<DownloadQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadQueueService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Serve downloaded files
app.MapGet("/api/download/{jobId:guid}", async (Guid jobId, DownloadQueueService queue) =>
{
    var jobs = await queue.GetAllAsync();
    var job  = jobs.FirstOrDefault(j => j.Id == jobId);
    if (job is null) return Results.NotFound();
    if (job.Status != DownloadStatus.Ready || job.FilePath is null) return Results.StatusCode(202);
    if (!File.Exists(job.FilePath)) return Results.NotFound("File not found on disk");

    var contentType = string.IsNullOrWhiteSpace(job.ContentType) ? "application/octet-stream" : job.ContentType;
    return Results.File(job.FilePath, contentType, job.DownloadName ?? Path.GetFileName(job.FilePath));
});

// First Ctrl+C → graceful. Second → force exit.
var ctrlCCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    if (++ctrlCCount > 1) { Console.Error.WriteLine("Force exit."); Environment.Exit(1); }
    e.Cancel = true;
    app.Lifetime.StopApplication();
};

app.Run();
