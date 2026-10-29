using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MyHomeLib.Web.Models;

namespace MyHomeLib.Web.Services;

/// <summary>
/// Keeps crawlers off the download links, in front of <see cref="TorrServe.TorrServeThrottle"/>.
/// </summary>
/// <remarks>
/// The throttle protects TorrServe, but it is first come, first served: one crawler walking
/// /m/book pages can hold every slot, and then readers get 503s. Three layers stop that
/// before a request reaches the throttle:
/// <list type="bullet">
/// <item><c>robots.txt</c> asks well-behaved crawlers not to follow /download and /api.</item>
/// <item>A crawler user agent on a download URL gets 403 without touching TorrServe.</item>
/// <item>Each client address gets a token bucket: a burst big enough for a whole series,
/// refilled slowly. Beyond it, 429.</item>
/// </list>
/// Reader apps and e-ink browsers do not identify as bots, so the agent check costs them
/// nothing. A person grabbing a series spends the burst once and then pauses; a crawler
/// keeps going and is held to the refill rate.
/// </remarks>
public static class DownloadGuard
{
    /// <summary>Rate limiter policy applied to every download endpoint.</summary>
    public const string PolicyName = "download";

    private const string RobotsTxt = """
        User-agent: *
        Disallow: /download/
        Disallow: /api/

        """;

    /// <summary>
    /// User-agent fragments that only crawlers and scripted clients send. "bot" covers
    /// Googlebot, bingbot, GPTBot, ClaudeBot, AhrefsBot, YandexBot, PetalBot and most others.
    /// </summary>
    private static readonly string[] CrawlerAgents =
    [
        "bot",
        "crawl",
        "spider",
        "slurp",
        "scrapy",
        "python-requests",
        "go-http-client",
        "wget",
        "curl/",
        "headless",
    ];

    public static bool IsCrawler(string? userAgent) =>
        !string.IsNullOrWhiteSpace(userAgent)
        && CrawlerAgents.Any(agent => userAgent.Contains(agent, StringComparison.OrdinalIgnoreCase));

    public static IServiceCollection AddDownloadGuard(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
                    : 60;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                return ValueTask.CompletedTask;
            };

            options.AddPolicy(PolicyName, context =>
            {
                var config = context.RequestServices.GetRequiredService<IOptions<TorrentConfig>>().Value;

                // 0 switches the per-client limit off; the TorrServe throttle still applies.
                if (config.DownloadBurstPerClient <= 0)
                    return RateLimitPartition.GetNoLimiter("unlimited");

                // One token every 60/rate seconds rather than the whole rate once a minute, so
                // the bucket refills smoothly instead of in a lump a crawler can time.
                var perMinute = Math.Max(1, config.DownloadsPerMinutePerClient);
                var client = context.Connection.RemoteIpAddress ?? IPAddress.None;
                return RateLimitPartition.GetTokenBucketLimiter(client.ToString(), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = config.DownloadBurstPerClient,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / perMinute),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }

    public static IApplicationBuilder UseDownloadGuard(this WebApplication app)
    {
        app.MapGet("/robots.txt", () => Results.Text(RobotsTxt, "text/plain"));

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/download")
                && IsCrawler(context.Request.Headers.UserAgent.ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });

        app.UseRateLimiter();
        return app;
    }
}
