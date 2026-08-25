using System.Text;
using Microsoft.Extensions.Options;
using MyHomeLib.Web;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Search;
using MyHomeLib.Web.Services.TorrServe;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.Configure<TorrentConfig>(builder.Configuration.GetSection("Torrent"));
builder.Services.Configure<LibraryConfig>(builder.Configuration.GetSection("Library"));
builder.Services.Configure<MeiliConfig>(builder.Configuration.GetSection("Meili"));

// CORS — allow any origin for /api/* so the standalone HTML build works from
// file:// or any external host. Credentials are not used cross-origin.
builder.Services.AddCors(options =>
    options.AddPolicy("Api", policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
        // Expose Content-Disposition for filename in cross-origin responses
        .WithExposedHeaders("Content-Disposition")));

builder.Services.AddYarpProxy(builder.Configuration);

// TorrServe services
builder.Services.AddHttpClient<TorrServeClient>((sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<TorrentConfig>>().Value.TorrServeUrl))
    .ConfigurePrimaryHttpMessageHandler(Ipv4FirstHandler.Create);
builder.Services.AddTransient<DownloadManager>();
builder.Services.AddTransient<BookDownloadService>();

// Search: Meilisearch-backed index built at startup from the configured library.
builder.Services.AddHttpClient<MeiliClient>((sp, c) =>
    {
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MeiliConfig>>().Value.Url);
        // Bulk indexing tasks are awaited over HTTP; the default 100s is ample but explicit here.
        c.Timeout = TimeSpan.FromMinutes(5);
    })
    .ConfigurePrimaryHttpMessageHandler(Ipv4FirstHandler.Create);
builder.Services.AddSingleton<IndexState>();
builder.Services.AddSingleton<GenreLabelService>();
builder.Services.AddScoped<LibrarySearchService>();
builder.Services.AddHostedService<LibraryIndexerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseCors();

app.UseRequestLocalization(options =>
{
    string[] supported = ["en", "ru"];
    options.SetDefaultCulture("en")
           .AddSupportedCultures(supported)
           .AddSupportedUICultures(supported);
    options.FallBackToParentCultures = true;
    options.FallBackToParentUICultures = true;
});

// The SPA is only present when MyHomeLib.Ui has actually been built into wwwroot. The
// repo carries a placeholder index.html referencing hashed bundles that do not exist, so
// serving it unconditionally produces a blank page and two 404s rather than an app.
var spaAssetsPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "assets");
var hasSpaBundle = Directory.Exists(spaAssetsPath) && Directory.EnumerateFiles(spaAssetsPath).Any();

if (!app.Environment.IsDevelopment() && hasSpaBundle)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapControllers();

app.Map("/error", () => Results.Problem("Internal server error."));

app.MapFallback("/api/{**catch-all}", () => Results.NotFound());

app.MapYarpProxy();

if (hasSpaBundle)
{
    if (!app.Environment.IsDevelopment())
        app.MapFallbackToFile("index.html");
}
else
{
    // No SPA: send the root at the server-rendered pages, which always work.
    app.MapGet("/", () => Results.Redirect("/m"));
    app.Logger.LogInformation(
        "No SPA bundle in {Path} — serving the thin UI at /m and the OPDS catalog at /opds", spaAssetsPath);
}

// First Ctrl+C → graceful. Second → force exit.
var ctrlCCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    if (++ctrlCCount > 1) { Console.Error.WriteLine("Force exit."); Environment.Exit(1); }
    e.Cancel = true;
    app.Lifetime.StopApplication();
};

app.Run();
