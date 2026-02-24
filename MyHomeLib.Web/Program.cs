using System.Text;
using Microsoft.AspNetCore.Http;
using MyHomeLib.Web;
using MyHomeLib.Web.Components;
using MyHomeListServer.Torrent;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserSessionContext>();
builder.Services.AddScoped<SearchPageState>();
builder.Services.AddSingleton<DatabaseMigrationService>();

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
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<LibraryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryService>());

builder.Services.AddSingleton<DownloadQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadQueueService>());

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseMigrationService>().MigrateAsync();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.Use(async (context, next) =>
{
    if (!context.Request.Cookies.ContainsKey(UserSessionCookie.CookieName))
    {
        context.Response.Cookies.Append(UserSessionCookie.CookieName, UserSessionCookie.NewUserId(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(10),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        });
    }

    await next();
});

app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Serve downloaded files
app.MapGet("/api/download/{jobId:guid}", async (Guid jobId, HttpContext httpContext, DownloadQueueService queue, AuditService audit) =>
{
    var userId = httpContext.Request.Cookies[UserSessionCookie.CookieName];
    if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

    var job  = await queue.GetByIdAsync(jobId, userId);
    if (job is null) return Results.NotFound();
    if (job.Status != DownloadStatus.Ready || job.FilePath is null) return Results.StatusCode(202);
    if (!File.Exists(job.FilePath)) return Results.NotFound("File not found on disk");

    _ = audit.LogDownloadAsync(jobId, job.Title, job.DownloadName ?? Path.GetFileName(job.FilePath));
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
