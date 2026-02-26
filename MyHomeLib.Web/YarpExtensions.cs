using System.Diagnostics;

namespace MyHomeLib.Web;

internal static class YarpExtensions
{
    [Conditional("DEBUG")]
    public static void AddYarpProxy(this IServiceCollection services, IConfiguration configuration)
    {
#if DEBUG
        services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));
#endif
    }

    [Conditional("DEBUG")]
    public static void MapYarpProxy(this WebApplication app)
    {
#if DEBUG
        if (app.Environment.IsDevelopment())
            app.MapReverseProxy();
#endif
    }
}
