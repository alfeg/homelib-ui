using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using MyHomeLib.Web;
using MyHomeListServer.Torrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
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

builder.Services.AddSingleton<LibraryBooksCacheService>();
builder.Services.AddSingleton<IdleTorrentCleanupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IdleTorrentCleanupService>());

var app = builder.Build();

var spaDistPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "MyHomeLib.Ui", "dist"));
var hasSpaDist = Directory.Exists(spaDistPath);
var spaIndexRoot = hasSpaDist ? spaDistPath : app.Environment.WebRootPath;

if (hasSpaDist)
    app.Logger.LogInformation("Serving SPA static assets from {SpaDistPath}", spaDistPath);
else
    app.Logger.LogInformation("SPA dist path {SpaDistPath} not found. Falling back to wwwroot ({WebRootPath}).", spaDistPath, app.Environment.WebRootPath);

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

if (hasSpaDist)
{
    var spaDistProvider = new PhysicalFileProvider(spaDistPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = spaDistProvider,
        RequestPath = ""
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = spaDistProvider,
        RequestPath = ""
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/favicon.ico", () => Results.NoContent());

app.MapPost("/api/library/inpx", async (
    LibraryBooksRequest request,
    LibraryBooksCacheService booksCache,
    IdleTorrentCleanupService idleTorrentCleanupService,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MagnetUri))
        return Results.BadRequest("magnetUri is required.");

    idleTorrentCleanupService.MarkActivity(request.MagnetUri);

    if (request.ForceReindex)
        logger.LogDebug("forceReindex is ignored for /api/library/inpx.");

    try
    {
        var inpxFile = await booksCache.GetInpxFileAsync(request.MagnetUri, ct);
        return Results.File(inpxFile.Data, "application/octet-stream", inpxFile.FileName);
    }
    catch (FormatException)
    {
        return Results.BadRequest("Invalid magnetUri.");
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest("Unable to prepare INPX file.");
    }
    catch (HttpRequestException)
    {
        return Results.Text("TorrServe is unavailable. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.Text("TorrServe is unavailable. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TimeoutException)
    {
        return Results.Text("TorrServe is unavailable. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error in /api/library/inpx.");
        return Results.Text("Internal server error.", statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/library/download", async (
    LibraryDirectDownloadRequest request,
    DownloadManager downloadManager,
    IdleTorrentCleanupService idleTorrentCleanupService,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MagnetUri)
        || string.IsNullOrWhiteSpace(request.ArchiveFile)
        || string.IsNullOrWhiteSpace(request.File)
        || string.IsNullOrWhiteSpace(request.Ext))
    {
        return Results.BadRequest("magnetUri, archiveFile, file and ext are required.");
    }

    idleTorrentCleanupService.MarkActivity(request.MagnetUri);

    try
    {
        var hash = MagnetUriHelper.ParseInfoHash(request.MagnetUri);
        var ext = request.Ext.TrimStart('.');
        var fileName = $"{request.File}.{ext}";

        var response = await downloadManager.DownloadFile(
            new DownloadRequest(hash, request.ArchiveFile, fileName) { MagnetUri = request.MagnetUri }, ct);

        if (response is null)
            return Results.NotFound();

        var contentType = string.IsNullOrWhiteSpace(response.ContentType)
            ? "application/octet-stream"
            : response.ContentType;

        var friendlyBase = string.IsNullOrWhiteSpace(request.Title)
            ? request.File
            : string.IsNullOrWhiteSpace(request.Authors)
                ? request.Title
                : $"{request.Authors} - {request.Title}";

        var downloadName = string.IsNullOrWhiteSpace(response.Name)
            ? MakeSafeFileName($"{friendlyBase}.{ext}")
            : response.Name;

        return Results.File(response.Data, contentType, downloadName);
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (HttpRequestException)
    {
        return Results.Text("TorrServe is unavailable. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.Text("TorrServe is unavailable. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TimeoutException)
    {
        return Results.Text("TorrServe is unavailable. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error in /api/library/download.");
        return Results.Text("Internal server error.", statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapFallback(async context =>
{
    if (!HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || Path.HasExtension(context.Request.Path.Value))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(spaIndexRoot, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("SPA index.html not found. Build MyHomeLib.Ui/dist or provide wwwroot/index.html.");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
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

static string MakeSafeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
}

