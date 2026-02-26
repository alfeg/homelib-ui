using System.Text;
using Microsoft.Extensions.Options;
using MyHomeLib.Web;
using MyHomeLib.Web.Models;
using MyHomeLib.Web.Services;
using MyHomeLib.Web.Services.TorrServe;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.Configure<TorrentConfig>(builder.Configuration.GetSection("Torrent"));

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
    c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<TorrentConfig>>().Value.TorrServeUrl));
builder.Services.AddTransient<DownloadManager>();

builder.Services.AddMemoryCache();
builder.Services.AddTransient<IdleTorrentCleanupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseCors();

if (!app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapControllers();

app.MapFallback("/api/{**catch-all}", () => Results.NotFound());

app.MapYarpProxy();

if (!app.Environment.IsDevelopment())
    app.MapFallbackToFile("index.html");

// First Ctrl+C → graceful. Second → force exit.
var ctrlCCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    if (++ctrlCCount > 1) { Console.Error.WriteLine("Force exit."); Environment.Exit(1); }
    e.Cancel = true;
    app.Lifetime.StopApplication();
};

app.Run();

