using System.IO.Compression;
using System.Text;
using MessagePack;
using Microsoft.AspNetCore.Http;
using MyHomeLib.Web;
using MyHomeListServer.Torrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
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

builder.Services.AddSingleton<LibraryBooksCacheService>();
builder.Services.AddSingleton<IdleTorrentCleanupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IdleTorrentCleanupService>());

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseMigrationService>().MigrateAsync();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.Use(async (context, next) =>
{
    if (!context.Request.Cookies.ContainsKey(UserSessionCookie.CookieName))
    {
        context.Response.Cookies.Append(UserSessionCookie.CookieName, UserSessionCookie.NewUserId(), BuildUserCookieOptions(context));
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/favicon.ico", () => Results.NoContent());

app.MapGet("/api/session/user-id", (HttpContext httpContext) =>
{
    var userId = httpContext.Request.Cookies[UserSessionCookie.CookieName];
    if (string.IsNullOrWhiteSpace(userId))
    {
        userId = UserSessionCookie.NewUserId();
        httpContext.Response.Cookies.Append(UserSessionCookie.CookieName, userId, BuildUserCookieOptions(httpContext));
    }

    return Results.Ok(new { userId });
});

app.MapPost("/api/library/books", async (
    LibraryBooksRequest request,
    LibraryBooksCacheService booksCache,
    IdleTorrentCleanupService idleTorrentCleanupService,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MagnetUri))
        return Results.BadRequest("magnetUri is required.");

    idleTorrentCleanupService.MarkActivity(request.MagnetUri);

    try
    {
        var response = await booksCache.GetBooksAsync(request.MagnetUri, request.ForceReindex, ct);
        return Results.Ok(response);
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(ex.Message);
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
        logger.LogError(ex, "Unhandled error in /api/library/books.");
        return Results.Text("Internal server error.", statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/library/books/msgpack", async (
    HttpContext httpContext,
    LibraryBooksRequest request,
    LibraryBooksCacheService booksCache,
    IdleTorrentCleanupService idleTorrentCleanupService,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MagnetUri))
        return Results.BadRequest("magnetUri is required.");

    idleTorrentCleanupService.MarkActivity(request.MagnetUri);

    try
    {
        var response = await booksCache.GetBooksAsync(request.MagnetUri, request.ForceReindex, ct);
        var minimizedPayload = response.ToMsgPack();

        var msgPackBytes = MessagePackSerializer.Serialize(minimizedPayload);
        var compressedBytes = BrotliCompress(msgPackBytes);

        httpContext.Response.Headers.ContentEncoding = "br";
        return Results.File(compressedBytes, "application/msgpack");
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(ex.Message);
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
        logger.LogError(ex, "Unhandled error in /api/library/books/msgpack.");
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
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
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

static CookieOptions BuildUserCookieOptions(HttpContext context) => new()
{
    Expires = DateTimeOffset.UtcNow.AddYears(10),
    HttpOnly = true,
    IsEssential = true,
    SameSite = SameSiteMode.Lax,
    Secure = context.Request.IsHttps
};


static string MakeSafeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
}

static byte[] BrotliCompress(byte[] input)
{
    using var output = new MemoryStream();
    using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
    {
        brotli.Write(input, 0, input.Length);
    }

    return output.ToArray();
}
