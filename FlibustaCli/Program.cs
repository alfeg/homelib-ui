// See https://aka.ms/new-console-template for more information

// library
//     list
//     add
// search: book
// download: book id

using FlibustaCli.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyHomeListServer.Torrent;
using Spectre.Console;
using Spectre.Console.Cli;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddJsonFile("./appsettings.json", optional: true)
    .Build();

var services = new ServiceCollection()
    .AddLogging()
    .AddTorrents(configuration);


var typeRegistrar = new TypeRegistrar(services);
var app = new CommandApp(typeRegistrar);

var appConfig = configuration.Get<AppConfig>();

AnsiConsole.Write(new FigletText("Flibusta CLI"));
AnsiConsole.MarkupLineInterpolated($"[grey][bold]CacheDirectory[/] is[/]: [silver]{Path.GetFullPath(appConfig.CacheDirectory)}[/]");
AnsiConsole.WriteLine();

app.Configure(config =>
{
    config.SetApplicationName("FlibustaCLI");
   
    // config.ValidateExamples();
    
    config.AddExample("lib", "list");
    config.AddExample("lib", "add", "magnet:?xt=urn:btih:86754c4ea0c0bc40d6f3b260ac8476d4cdec5591&tr=http%3A%2F%2Ftr.ysagin.top%3A2710%2Fannounce&xl=426504503079&dn=Flibusta.FB2.01.05.23");
    config.AddExample("search", "Колесо времени");
    config.AddExample("download", "123");

    config.AddBranch<LibraryCommandSettings>("lib", lib =>
    {
        lib.AddCommand<ListLibraryCommand>("list")
            .WithDescription("List added libraries");
        lib.AddCommand<AddLibraryCommand>("lib-add").WithDescription("Add library with magnet uri");    
    }).WithAlias("library");

    config.AddCommand<ListLibraryCommand>("lib-list").WithDescription("List added libraries");
    config.AddCommand<AddLibraryCommand>("lib-add").WithDescription("Add library with magnet uri");
    
    config.AddCommand<SearchCommand>("search");
    config.AddCommand<DownloadCommand>("download").WithAlias("get");
    config.Settings.ShowOptionDefaultValues = true;
});

await app.RunAsync(args);