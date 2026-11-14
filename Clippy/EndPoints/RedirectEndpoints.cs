using Clippy.Extensions;
using Clippy.Services.Analytics;
using Clippy.Services.Caching;
namespace Clippy.EndPoints;
public static class RedirectEndpoints
{
    private static readonly HashSet<string> IgnoredSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "auth", "account", "login", "register", "logout", "dashboard",
        "analytics", "admin", "error", "health", "signin-google", "signin-facebook",
        "signin-oidc", "_framework", "_blazor", "favicon.png", "robots.txt"
    };
    public static IEndpointRouteBuilder MapClippyRedirectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/{shortCode:regex(^[a-zA-Z0-9\\-_]{{3,32}}$)}", async (string shortCode, HttpContext httpContext,
            IUrlResolutionCache resolutionCache, IClickTelemetryQueue telemetryQueue, CancellationToken ct) =>
        {
            if (IgnoredSegments.Contains(shortCode))
            {
                return Results.NotFound();
            }
            var resolved = await resolutionCache.ResolveShortCodeAsync(shortCode, ct);
            switch (resolved.Status)
            {
                case RedirectResolutionStatus.NotFound:
                    return Results.NotFound(new
                    {
                        error = "Link Not Found",
                        message = $"No shortened link found for code '{shortCode}'."
                    });
                case RedirectResolutionStatus.Expired:
                    return Results.Json(new
                    {
                        error = "Link Expired",
                        message = "This Shortened Link is expired and no longer available"
                    }, statusCode: StatusCodes.Status410Gone);
                case RedirectResolutionStatus.BlockedOrInactive:
                    return Results.Json(new
                    {
                        error = "LinkBlocked",
                        message = "This link has been deactivated or flagged by Clippy Security."
                    }, statusCode: StatusCodes.Status403Forbidden);
                case RedirectResolutionStatus.Found:
                    var clientIp = ExtractClientIp(httpContext);
                    var userAgent = httpContext.Request.Headers.UserAgent.ToString();
                    var referer = httpContext.Request.Headers.Referer.ToString();
                    var country = httpContext.Request.Headers["CF-IPCountry"].FirstOrDefault() ?? httpContext.Request.Headers["X-Country-Code"].FirstOrDefault();
                    telemetryQueue.EnqueueClick(new clickTelemetryEvent(
                    resolved.ShortUrlId,
                    resolved.ShortCode ?? shortCode,
                    DateTime.UtcNow,
                    clientIp,
                    userAgent,
                    referer,
                    country));
                    httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
                    return Results.Redirect(resolved.DestinationUrl!, permanent: false);
                default:
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }).RequireRateLimiting(RateLimitExtensions.RedirectPolicy).AllowAnonymous();
        return app;
    }
    private static string ExtractClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }
}