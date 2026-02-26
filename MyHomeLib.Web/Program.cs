using System.Text;
using MyHomeLib.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.Configure<LibraryConfig>(builder.Configuration.GetSection("Library"));

// CORS — allow any origin for /api/* so the standalone HTML build works from
// file:// or any external host. Credentials are not used cross-origin.
builder.Services.AddCors(options =>
    options.AddPolicy("Api", policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
        // Expose Content-Disposition for filename in cross-origin responses
        .WithExposedHeaders("Content-Disposition")));

#if DEBUG
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
}
#endif

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

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseCors();

if (!app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapPost("/api/library/inpx", async (
    LibraryBooksRequest request,
    HttpContext httpContext,
    LibraryBooksCacheService booksCache,
    IdleTorrentCleanupService idleTorrentCleanupService,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MagnetUri))
        return Results.BadRequest(L(httpContext, "magnetUri is required.", "Требуется magnetUri."));

    idleTorrentCleanupService.MarkActivity(request.MagnetUri);

    if (request.ForceReindex)
        logger.LogDebug("forceReindex is ignored for /api/library/inpx");

    try
    {
        var inpxFile = await booksCache.GetInpxFileAsync(request.MagnetUri, ct);
        return Results.File(inpxFile.Data, "application/octet-stream", inpxFile.FileName);
    }
    catch (FormatException)
    {
        return Results.BadRequest(L(httpContext, "Invalid magnetUri.", "Некорректный magnetUri."));
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest(L(httpContext, "Unable to prepare INPX file.", "Не удалось подготовить INPX файл."));
    }
    catch (HttpRequestException)
    {
        return Results.Text(L(httpContext, "TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.Text(L(httpContext, "TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TimeoutException)
    {
        return Results.Text(L(httpContext, "TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error in /api/library/inpx");
        return Results.Text(L(httpContext, "Internal server error.", "Внутренняя ошибка сервера."), statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireCors("Api");

app.MapPost("/api/library/download", async (
    LibraryDirectDownloadRequest request,
    HttpContext httpContext,
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
        return Results.BadRequest(L(httpContext, "magnetUri, archiveFile, file and ext are required.", "Требуются поля magnetUri, archiveFile, file и ext."));
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
        return Results.BadRequest(string.IsNullOrWhiteSpace(ex.Message)
            ? L(httpContext, "Invalid request.", "Некорректный запрос.")
            : ex.Message);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(string.IsNullOrWhiteSpace(ex.Message)
            ? L(httpContext, "File not found.", "Файл не найден.")
            : ex.Message);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(string.IsNullOrWhiteSpace(ex.Message)
            ? L(httpContext, "Unable to process download request.", "Не удалось обработать запрос на скачивание.")
            : ex.Message);
    }
    catch (HttpRequestException)
    {
        return Results.Text(L(httpContext, "TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
    {
        return Results.Text(L(httpContext, "TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TimeoutException)
    {
        return Results.Text(L(httpContext, "TorrServe is unavailable. Please try again later.", "TorrServe недоступен. Попробуйте позже."), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error in /api/library/download");
        return Results.Text(L(httpContext, "Internal server error.", "Внутренняя ошибка сервера."), statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireCors("Api");

app.MapFallback("/api/{**catch-all}", () => Results.NotFound());

if (app.Environment.IsDevelopment())
{
#if DEBUG
    app.MapReverseProxy();
#endif
}
else
{
    app.MapFallbackToFile("index.html");
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

static string MakeSafeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
}

static string L(HttpContext context, string en, string ru)
{
    var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
    return acceptLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? ru : en;
}
