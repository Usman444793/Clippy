using System.Threading.RateLimiting;
namespace Clippy.Extensions;
public static class RateLimitExtensions
{
    public const string OtpPolicy = "OtpRateLimitPolicy";
    public const string ShortenPolicy = "ShortenUrlRateLimitPolicy";
    public const string RedirectPolicy = "RedirectRateLimitPolicy";
    public static IServiceCollection AddClippyRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? (int)retryAfter.TotalSeconds : 60;
                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests",
                    message = $"You have exceeded the rate limit.Please try again after {retryAfterSeconds}.",
                    retryAfterSeconds
                }, cancellationToken);
            };
            options.AddPolicy(OtpPolicy, httpContext =>
            {
            var clientIp = GetClientIpAddress(httpContext);
            return RateLimitPartition.GetFixedWindowLimiter
                (partitionKey:$"otp:{clientIp}",factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromSeconds(60),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });
            options.AddPolicy(ShortenPolicy, httpContext =>
            {
                var clientIp = GetClientIpAddress(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter
                (partitionKey: $"shorten: {clientIp}",factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 15,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 3,
                    QueueLimit = 0
                });
            });
            options.AddPolicy(RedirectPolicy, httpContext =>
            {
                var clientIp = GetClientIpAddress(httpContext);
                return RateLimitPartition.GetTokenBucketLimiter(partitionKey: $"redirect : {clientIp}", factory: _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 200,
                    TokensPerPeriod = 100,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true,
                    QueueLimit = 0
                });
            });
        });
        return services;
    }
    private static string GetClientIpAddress(HttpContext context)
    {
        if(context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardFor))
        {
            var firstIp = forwardFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstIp))
            {
                return firstIp.Trim();
            }
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
    }
}