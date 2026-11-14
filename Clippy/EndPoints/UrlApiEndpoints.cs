using System.Security.Claims;
using Clippy.Client.Models;
using Clippy.Extensions;
using Clippy.Services.Shortening;
using Microsoft.AspNetCore.Mvc;
namespace Clippy.EndPoints;
public static class UrlApiEndpoints
{
    public static IEndpointRouteBuilder MapClippyUrlApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/urls");
        group.MapPost("/", async ([FromBody] CreateShortUrlRequest request, HttpContext httpContext, IUrlShortenerService shortnerService, CancellationToken ct = default) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var result = await shortnerService.CreateShortUrlAsync(request, userId, baseUrl, ct);
            if (!result.Succeeded || result.Data is null)
            {
                return Results.BadRequest(new { error = result.ErrorMessage ?? "Failed to shorten URL." });
            }
            return Results.Ok(result.Data);
        }).RequireRateLimiting(RateLimitExtensions.ShortenPolicy).AllowAnonymous();
        group.MapGet("/my", async (HttpContext httpContext, IUrlShortenerService shortenerService, CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var links = await shortenerService.GetUserUrlsAsync(userId, baseUrl, ct);
            return Results.Ok(links);
        }).RequireAuthorization();
        group.MapGet("/recent", async (HttpContext httpContext, IUrlShortenerService shortenerService, CancellationToken ct) =>
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var links = await shortenerService.GetRecentPublicUrlsAsync(5, baseUrl, ct);
            return Results.Ok(links);
        }).AllowAnonymous();
        group.MapGet("/{code}/analytics", async (string code, HttpContext httpContext, IUrlShortenerService shortenerService, CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var summary = await shortenerService.GetAnalyticsAsync(code, userId, ct);
            return summary is not null ? Results.Ok(summary) : Results.NotFound(new { error = "Analytics not found for this code" });
        }).AllowAnonymous();
        group.MapDelete("/{id:long}", async (long id, HttpContext httpContext, IUrlShortenerService shortenerService, CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }
            var deleted = await shortenerService.DeleteUrlAsync(id, userId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization();
        return app;
    }
}
