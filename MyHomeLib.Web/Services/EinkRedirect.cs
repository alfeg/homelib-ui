namespace MyHomeLib.Web.Services;

/// <summary>
/// Sends e-reader browsers to the server-rendered pages instead of the SPA.
/// </summary>
/// <remarks>
/// A PocketBook-class browser cannot usefully run the Vue app — it is an old WebKit on a
/// slow CPU behind a screen that repaints in whole pages — so landing on the SPA looks like
/// a broken site rather than a heavy one. Only the site root redirects, and only for a GET:
/// every other URL stays reachable, so a link into the SPA still works if someone wants it.
///
/// The redirect is deliberately escapable and deliberately temporary:
/// <list type="bullet">
/// <item>302, never 301 — a permanent redirect would be cached on the device forever.</item>
/// <item><c>?full=1</c> opts out, so the SPA is always reachable from the same device.</item>
/// <item><c>Vary: User-Agent</c> keeps a shared cache from serving one device the other
/// device's answer.</item>
/// </list>
/// </remarks>
public static class EinkRedirect
{
    /// <summary>Query flag that suppresses the redirect for one request.</summary>
    public const string OptOutQuery = "full";

    /// <summary>
    /// User-agent fragments belonging to e-ink readers. PocketBook firmware reports either
    /// the brand or Obreey, the company that makes them; the rest are the other readers with
    /// a browser people actually use.
    /// </summary>
    private static readonly string[] ReaderAgents =
    [
        "pocketbook",
        "obreey",
        "kindle",
        "kobo",
        "onyx",
        "boox",
        "remarkable",
    ];

    public static bool IsEinkReader(string? userAgent) =>
        !string.IsNullOrWhiteSpace(userAgent)
        && ReaderAgents.Any(agent => userAgent.Contains(agent, StringComparison.OrdinalIgnoreCase));

    public static IApplicationBuilder UseEinkRedirect(this IApplicationBuilder app, string target = "/m")
    {
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("MyHomeLib.Web.EinkRedirect");

        return app.Use(async (context, next) =>
        {
            var request = context.Request;

            var isRoot = request.Path == "/" || request.Path == "";
            var optedOut = request.Query.ContainsKey(OptOutQuery);
            var userAgent = request.Headers.UserAgent.ToString();
            var isReader = IsEinkReader(userAgent);

            // Logged at the root only, and at Debug: matching a device is guesswork based on
            // a user-agent substring, so when a reader does not get redirected the first
            // question is always what it actually sent.
            if (isRoot)
            {
                logger.LogDebug("[UA] reader={IsReader} optedOut={OptedOut} ua={UserAgent}",
                    isReader, optedOut, string.IsNullOrEmpty(userAgent) ? "(none)" : userAgent);
            }

            if (isRoot && HttpMethods.IsGet(request.Method) && !optedOut && isReader)
            {
                context.Response.Headers.Vary = "User-Agent";
                context.Response.Redirect(target, permanent: false);
                return;
            }

            // Any cached copy of the root must remember which agent it was built for.
            if (isRoot) context.Response.Headers.Vary = "User-Agent";

            await next();
        });
    }
}
