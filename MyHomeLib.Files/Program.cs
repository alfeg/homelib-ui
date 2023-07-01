using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using MonoTorrent.Client;
using MyHomeLib.Files.Torrents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<AppConfig>(builder.Configuration);
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddSingleton<ClientEngine>(sp =>
{
    var config = sp.GetRequiredService<IOptions<AppConfig>>().Value;
    var settingsBuilder = new EngineSettingsBuilder()
    {
        FastResumeMode = FastResumeMode.Accurate,
        CacheDirectory = config.CacheDirectory,
    };

    var engine = new ClientEngine(settingsBuilder.ToSettings());
    // engine.StatsUpdate += (sender, eventArgs) =>
    // {
    //     var logger = sp.GetRequiredService<ILogger<Program>>();
    //     logger.LogInformation("Engine: {State}: Downloading: {EngineTotalDownloadSpeed}, Uploading: {EngineTotalUploadSpeed}",
    //         engine.IsRunning,
    //         engine.TotalDownloadSpeed, engine.TotalUploadSpeed);
    // };
    return engine;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedHost
                               | ForwardedHeaders.XForwardedProto;
});

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var app = builder.Build();

app.UseForwardedHeaders();
app.MapControllers();

app.Run();