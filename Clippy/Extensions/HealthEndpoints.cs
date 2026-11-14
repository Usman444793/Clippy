using System.Diagnostics;
using Clippy.Data;
using Clippy.Services.Shortening;
using Clippy.Tests;
using Microsoft.EntityFrameworkCore;

namespace Clippy.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapClippyHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (IDbContextFactory<ApplicationDbContext> dbFactory, IShortCodeGenerator idGenerator, CancellationToken ct) =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var canConnectDb = await db.Database.CanConnectAsync(ct);
                var sampleSnowflake = idGenerator.NextId();
                sw.Stop();
                if (!canConnectDb)
                {
                    return Results.Json(new
                    {
                        status = "Degraded",
                        sqlServer = "Unreachable",
                        latencyMs = sw.ElapsedMilliseconds
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                return Results.Ok(new
                {
                    status = "Healthy",
                    service = "Clippy Enterprise URL Engine (.NET 10)",
                    sqlServer = "Connected",
                    snowflakeNodeSample = sampleSnowflake.ShortCode,
                    probeLatencyMs = sw.ElapsedMilliseconds,
                    timestampUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    status = "Unhealthy",
                    error = ex.Message
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous();

        app.MapGet("/api/diagnostics/self-test", async () =>
        {
            var report = await RunEnterpriseSelfTestsAsync();
            return Results.Ok(report);
        })
        .AllowAnonymous();

        return app;
    }

    public static async Task<object> RunEnterpriseSelfTestsAsync()
    {
        var totalSw = Stopwatch.StartNew();
        var results = new List<object>();

        var stampedeTests = new CacheStampedeTests();
        var securityTests = new SecurityServicesTests();
        var snowflakeTests = new SnowflakeConcurrencyTests();

        async Task RunTestAsync(string name, Func<Task> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await action();
                sw.Stop();
                results.Add(new { test = name, status = "PASSED", durationMs = sw.ElapsedMilliseconds });
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new { test = name, status = "FAILED", durationMs = sw.ElapsedMilliseconds, error = ex.Message });
                throw;
            }
        }

        await RunTestAsync("CacheStampedeTests.ResolveShortCodeAsync_When500ConcurrentRequestsMissCache_QueriesDatabaseOnlyOnce",
            () => stampedeTests.ResolveShortCodeAsync_When500ConcurrentRequestsMissCache_QueriesDatabaseOnlyOnce());

        await RunTestAsync("CacheStampedeTests.ResolveShortCodeAsync_WhenLinkIsExpired_ReturnsHttp410GoneStatus",
            () => stampedeTests.ResolveShortCodeAsync_WhenLinkIsExpired_ReturnsHttp410GoneStatus());

        await RunTestAsync("SecurityServicesTests.OtpService_AfterThreeFailedGuesses_InvalidatesCodeEvenIfFourthGuessIsCorrect",
            () => { securityTests.OtpService_AfterThreeFailedGuesses_InvalidatesCodeEvenIfFourthGuessIsCorrect(); return Task.CompletedTask; });

        await RunTestAsync("SecurityServicesTests.UrlSafetyScanner_MaliciousAndSsrfVectors",
            () =>
            {
                securityTests.UrlSafetyScanner_WhenUrlIsMaliciousOrSsrf_ReturnsFlaggedMalicious("javascript:alert(document.cookie)");
                securityTests.UrlSafetyScanner_WhenUrlIsMaliciousOrSsrf_ReturnsFlaggedMalicious("http://127.0.0.1:8080/admin");
                securityTests.UrlSafetyScanner_WhenUrlIsMaliciousOrSsrf_ReturnsFlaggedMalicious("http://localhost:5000/secret");
                securityTests.UrlSafetyScanner_WhenUrlIsMaliciousOrSsrf_ReturnsFlaggedMalicious("https://bit.ly/3xyz");
                securityTests.UrlSafetyScanner_WhenUrlIsMaliciousOrSsrf_ReturnsFlaggedMalicious("https://evil.com/free-crypto-airdrop/login.exe");
                securityTests.UrlSafetyScanner_WhenValidExternalHttpsUrl_ReturnsVerifiedSafe();
                return Task.CompletedTask;
            });

        await RunTestAsync("SecurityServicesTests.UrlShortenerService_EnforcesStrictOwnerOnlyAuthorizationAndIsolation",
            () => securityTests.UrlShortenerService_EnforcesStrictOwnerOnlyAuthorizationAndIsolation());

        await RunTestAsync("SnowflakeConcurrencyTests.NextId_UnderHighParallelism_Generates100kUniqueIdsAndCodesWithZeroCollisions",
            () =>
            {
                snowflakeTests.NextId_UnderHighParallelism_Generates100kUniqueIdsAndCodesWithZeroCollisions();
                snowflakeTests.EncodeBase62_ProducesAlphanumericNonEmptyStrings(1L);
                snowflakeTests.EncodeBase62_ProducesAlphanumericNonEmptyStrings(61L);
                snowflakeTests.EncodeBase62_ProducesAlphanumericNonEmptyStrings(62L);
                snowflakeTests.EncodeBase62_ProducesAlphanumericNonEmptyStrings(987654321012345L);
                return Task.CompletedTask;
            });

        totalSw.Stop();
        return new
        {
            suite = "Clippy Enterprise xUnit Self-Test Suite",
            allPassed = true,
            totalTests = results.Count,
            totalDurationMs = totalSw.ElapsedMilliseconds,
            results
        };
    }
}
