using MudBlazor.Services;
using MyHomeLibServer.Data;

var builder = WebApplication.CreateBuilder(args);



void ConfigureServices(IServiceCollection services)
{
    services.AddMudServices();
    services.AddMvc();
    services.AddRazorPages();
    services.AddServerSideBlazor();
    services.Configure<LibraryConfig>(builder.Configuration.GetSection(LibraryConfig.Section));
    services.AddSingleton<LibraryAccessor>();
    services.AddHostedService<StorageInitializationHostedService>();
    services.AddHostedService<LibraryInitBgService>();
    services.AddScoped<LibrarySearch>();
    services.AddTransient<ImportDataService>();
    services.AddDbContextFactory<LibDbContext>();
    services.AddLocalization();
}

ConfigureServices(builder.Services);

var app = builder.Build();


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseRequestLocalization();

app.UseEndpoints(e =>
{
    e.MapControllers();
    e.MapBlazorHub();
    e.MapFallbackToPage("/_Host");
});

app.Run();
