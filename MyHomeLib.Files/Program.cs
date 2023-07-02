using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MyHomeLib.Files.Core;
using MyHomeLib.Files.Torrents;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic);
        options.JsonSerializerOptions.WriteIndented = true;   
    });;
builder.Services.Configure<AppConfig>(builder.Configuration);
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddSingleton<LibraryIndexer>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ClientEngine>(sp =>
{
    var config = sp.GetRequiredService<IOptions<AppConfig>>().Value;
    var settingsBuilder = new EngineSettingsBuilder()
    {
        FastResumeMode = FastResumeMode.BestEffort,
        CacheDirectory = config.CacheDirectory,
    };

    var factories = new Factories()
        .WithStreamingPieceRequesterCreator(() => new PartialStreamingRequester(config));
    
    var engine = new ClientEngine(settingsBuilder.ToSettings(), factories);
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