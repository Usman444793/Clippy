using Clippy.Data;
using Clippy.Entities;
using Clippy.Extensions;
using Clippy.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;
namespace Clippy.Tests;
public class CacheStampedeTests
{
    private sealed class CountingDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        private int _dbInvocationCount;
        public int DbInvocationCount => _dbInvocationCount;
        public ApplicationDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _dbInvocationCount);
            return new ApplicationDbContext(options);
        }
        public async Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _dbInvocationCount);
            await Task.Delay(40, cancellationToken);
            return new ApplicationDbContext(options);
        }
    }
    [Fact]
    public async Task ResolveShortCodeAsync_When500ConcurrentRequestsMissCache_QueriesDatabaseOnlyOnce()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: $"StampedeTestDb_{Guid.NewGuid()}").Options;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            seedDb.ShortUrls.Add(new ShortUrl
            {
                Id = 999001L,
                shortCode = "viral27",
                DestinationUrl = "https://github.com/Usman444793",
                IsActive = true,
                SafetyStatus = LinkSafetyStatus.VerifiedSafe
            });
            await seedDb.SaveChangesAsync();
        }
        var countingFactory = new CountingDbContextFactory(dbOptions);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new ShortenerConfigOptions { DefaultCacheDurationMinutes = 30 });
        var resolutionCache = new UrlResolutionCache(memoryCache, countingFactory, config);
        const int concurrentRequests = 500;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await barrier.Task;
            return await resolutionCache.ResolveShortCodeAsync("viral27");
        }).ToArray();
        barrier.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r =>
        {
            Assert.Equal(RedirectResolutionStatus.Found, r.Status);
            Assert.Equal("https://github.com/Usman444793", r.DestinationUrl);
        });
        Assert.Equal(1, countingFactory.DbInvocationCount);
    }
    [Fact]
    public async Task ResolveShortCodeAsync_WhenLinkIsExpired_ReturnsHttp410GoneStatus()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: $"ExpiredTestDb_{Guid.NewGuid()}").Options;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            seedDb.ShortUrls.Add(new ShortUrl
            {
                Id = 999002L,
                shortCode = "oldlink",
                DestinationUrl = "https://example.com",
                IsActive = true,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10),
                SafetyStatus = LinkSafetyStatus.VerifiedSafe
            });
            await seedDb.SaveChangesAsync();
        }
        var factory = new CountingDbContextFactory(dbOptions);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new ShortenerConfigOptions { DefaultCacheDurationMinutes = 30 });
        var resolutionCache = new UrlResolutionCache(memoryCache, factory, config);
        var result = await resolutionCache.ResolveShortCodeAsync("oldlink");
        Assert.Equal(RedirectResolutionStatus.Expired, result.Status);
    }
}