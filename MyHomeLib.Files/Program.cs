using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.HttpOverrides;
using MyHomeListServer.Torrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic);
        options.JsonSerializerOptions.WriteIndented = true;   
    });;


builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedHost
                               | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddTorrents(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.MapControllers();

app.Run();