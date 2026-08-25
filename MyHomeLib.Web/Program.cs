using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using MyHomeLib.Web;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.Search;
using MyHomeLib.Web.Services.TorrServe;

// The index command runs without a web host: it builds the search index and exits, so
// index population can be scripted instead of being a side effect of starting the server.
var indexOnly = LibraryCli.IsIndexCommand(args);

if (LibraryCli.IsHelpRequest(args))
{
    LibraryCli.PrintHelp();
    return LibraryCli.ExitSuccess;
}

var builder = WebApplication.CreateBuilder(args);

if (indexOnly && LibraryCli.WantsForce(args))
    builder.Configuration["Library:ForceReindex"] = "true";

builder.Services.AddControllersWithViews();

// Compress the text responses. This is what lets the views be written for humans rather
// than squeezed for bytes: indentation and comments cost almost nothing once gzipped, and
// the reader spends less time on the wire than it would parsing a hand-minified page.
// Nothing here is secret or authenticated, so the usual BREACH concern does not apply.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes =
    [
        .. ResponseCompressionDefaults.MimeTypes,
        // OPDS feeds are Atom, which the defaults do not cover.
        "application/atom+xml",
        "application/opensearchdescription+xml",
    ];
});

// Razor's default HtmlEncoder escapes every non-ASCII character to a numeric entity, which
// for a Cyrillic collection roughly doubles each page — a search page measured 12KB before
// and 24KB after. These pages target readers with a few kilobytes of budget, and they
// declare charset=utf-8, so allow the characters through as themselves. HTML-significant
// characters (< > & ") are still escaped.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
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
builder.Services.AddSingleton<LibraryIndexerService>();

// Only serve-mode runs it in the background; the index command drives it directly.
if (!indexOnly)
    builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryIndexerService>());

var app = builder.Build();

if (indexOnly)
{
    using var indexCancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; indexCancellation.Cancel(); };

    return await LibraryCli.RunIndexAsync(app.Services, indexCancellation.Token);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseResponseCompression();

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
// build inlines everything into a single index.html (vite-plugin-singlefile), so that file
// existing is the whole test — there is no assets directory to look for, and the repo
// carries no placeholder any more.
var spaIndexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
var hasSpaBundle = File.Exists(spaIndexPath);

// Ahead of static files, so an e-reader asking for "/" never gets handed the SPA.
if (hasSpaBundle) app.UseEinkRedirect();

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
        "No SPA bundle at {Path} — serving the thin UI at /m and the OPDS catalog at /opds", spaIndexPath);
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

return LibraryCli.ExitSuccess;
