using MudBlazor.Services;
using MyHomeLibServer.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMudServices();
builder.Services.AddMvc();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.Configure<LibraryConfig>(builder.Configuration.GetSection(LibraryConfig.Section));
builder.Services.AddSingleton<LibraryAccessor>();
builder.Services.AddHostedService<StorageInitializationHostedService>();
builder.Services.AddHostedService<LibraryInitBgService>();
builder.Services.AddScoped<LibrarySearch>();
builder.Services.AddTransient<ImportDataService>();
builder.Services.AddDbContextFactory<LibDbContext>();

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

//app.UseAuthentication();
//app.UseAuthorization();

app.UseEndpoints(e =>
{
    e.MapControllers();
    e.MapBlazorHub();
    e.MapFallbackToPage("/_Host");
});

app.Run();
