using MyHomeLib.Web;
using MyHomeLib.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<LibraryConfig>(builder.Configuration.GetSection("Library"));
builder.Services.AddSingleton<LibraryService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Trigger library loading at startup rather than on first request.
_ = app.Services.GetRequiredService<LibraryService>().LoadTask;

app.Run();
