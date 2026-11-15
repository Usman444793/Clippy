using Clippy.Client.Models;
using Clippy.Data;
using Clippy.Entities;
using Clippy.Extensions;
using Clippy.Services.Caching;
using Clippy.Services.QrCode;
using Clippy.Services.Security;
using Clippy.Services.Shortening;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clippy.Tests;

public class SecurityServicesTests
{
    [Fact]
    public void OtpService_AfterThreeFailedGuesses_InvalidatesCodeEvenIfFourthGuessIsCorrect()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new SecurityOptions
        {
            OtpHmacSecret = "UnitTestSecretKey_32BytesLength!!",
            OtpExpirationMinutes = 5,
            MaxOtpAttempts = 3
        });
        var otpService = new OtpService(cache, options, NullLogger<OtpService>.Instance);
        const string email = "test@clippy.be";
        var generated = otpService.GenerateOtp(email);
        var attempt1 = otpService.VerifyOtp(email, "000000");
        var attempt2 = otpService.VerifyOtp(email, "111111");
        var attempt3 = otpService.VerifyOtp(email, "222222");
        // 4th attempt uses the REAL generated code, but must fail because the OTP was evicted on attempt 3
        var attempt4WithRealCode = otpService.VerifyOtp(email, generated.rawCode);
        Assert.False(attempt1.IsValid);
        Assert.Equal(2, attempt1.RemainingAttempts);
        Assert.False(attempt2.IsValid);
        Assert.Equal(1, attempt2.RemainingAttempts);
        Assert.False(attempt3.IsValid);
        Assert.Equal(0, attempt3.RemainingAttempts);
        Assert.False(attempt4WithRealCode.IsValid);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://localhost:5000/secret")]
    [InlineData("https://bit.ly/3xyz")]
    [InlineData("https://evil.com/free-crypto-airdrop/login.exe")]
    public void UrlSafetyScanner_WhenUrlIsMaliciousOrSsrf_ReturnsFlaggedMalicious(string dangerousUrl)
    {
        var scanner = new UrlSafetyScanner();
        var result = scanner.InspectUrl(dangerousUrl, "https://clippy.io");
        Assert.Equal(LinkSafetyStatus.FlaggedMalicious, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void UrlSafetyScanner_WhenValidExternalHttpsUrl_ReturnsVerifiedSafe()
    {
        var scanner = new UrlSafetyScanner();
        var result = scanner.InspectUrl("https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10", "https://clippy.io");
        Assert.Equal(LinkSafetyStatus.VerifiedSafe, result.Status);
    }

    [Fact]
    public async Task UrlShortenerService_EnforcesStrictOwnerOnlyAuthorizationAndIsolation()
    {
        var dbName = $"Clippy_AuthIsolation_{Guid.NewGuid()}";
        var dbFactory = new SimpleInMemoryFactory(dbName);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new ShortenerConfigOptions { SnowflakeMachineId = 2, DefaultCacheDurationMinutes = 10 });
        var resolutionCache = new UrlResolutionCache(memoryCache, dbFactory, config);
        var service = new UrlShortenerService(
            dbFactory,
            new SnowFlakeBase62CodeGenerator(config),
            new UrlSafetyScanner(),
            new QrCodeService(),
            resolutionCache);

        // 1. Anonymous user cannot shorten URLs
        var anonAttempt = await service.CreateShortUrlAsync(new CreateShortUrlRequest
        {
            DestinationUrl = "https://dotnet.microsoft.com",
            CustomAlias = "anon-attempt"
        }, userId: null, baseUrl: "https://clippy.io");
        Assert.False(anonAttempt.Succeeded);

        // 2. Owner A creates a link
        var ownerACreate = await service.CreateShortUrlAsync(new CreateShortUrlRequest
        {
            DestinationUrl = "https://dotnet.microsoft.com",
            CustomAlias = "owner-a-link",
            Title = "Owner A Launch"
        }, userId: "user-alpha", baseUrl: "https://clippy.io");
        Assert.True(ownerACreate.Succeeded);
        Assert.NotNull(ownerACreate.Data);

        var linkId = ownerACreate.Data!.Id;

        // 3. Attacker User B tries to edit, pause, view analytics, or delete Owner A's link -> All must fail!
        var attackerEdit = await service.UpdateUrlAsync(linkId, new UpdateShortUrlRequest
        {
            DestinationUrl = "https://phishing-redirect.org",
            CustomAlias = "hijacked"
        }, userId: "user-beta-attacker", baseUrl: "https://clippy.io");
        Assert.False(attackerEdit.Succeeded);

        var attackerToggle = await service.ToggleUrlStatusAsync(linkId, userId: "user-beta-attacker");
        Assert.False(attackerToggle.Succeeded);

        var attackerAnalytics = await service.GetAnalyticsAsync("owner-a-link", userId: "user-beta-attacker");
        Assert.Null(attackerAnalytics);

        var attackerDelete = await service.DeleteUrlAsync(linkId, userId: "user-beta-attacker");
        Assert.False(attackerDelete);

        // 4. Owner A can edit and manage their own link
        var ownerEdit = await service.UpdateUrlAsync(linkId, new UpdateShortUrlRequest
        {
            DestinationUrl = "https://learn.microsoft.com",
            CustomAlias = "owner-a-updated",
            Title = "Updated by Owner A",
            IsActive = true
        }, userId: "user-alpha", baseUrl: "https://clippy.io");
        Assert.True(ownerEdit.Succeeded);
        Assert.Equal("owner-a-updated", ownerEdit.Data!.CustomAlias);
    }

    [Fact]
    public async Task UrlShortenerService_DateTimeKindUnspecifiedAndFullLifecycle_NormalizesToUtcAndHandlesQrAndAnalytics()
    {
        var dbName = $"Clippy_FullLifecycle_{Guid.NewGuid()}";
        var dbFactory = new SimpleInMemoryFactory(dbName);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var config = Options.Create(new ShortenerConfigOptions { SnowflakeMachineId = 3, DefaultCacheDurationMinutes = 10 });
        var resolutionCache = new UrlResolutionCache(memoryCache, dbFactory, config);
        var service = new UrlShortenerService(
            dbFactory,
            new SnowFlakeBase62CodeGenerator(config),
            new UrlSafetyScanner(),
            new QrCodeService(),
            resolutionCache);

        // 1. Create with DateTimeKind.Unspecified (simulating HTML <input type="date" /> binding)
        var unspecifiedExpiry = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);
        var created = await service.CreateShortUrlAsync(new CreateShortUrlRequest
        {
            DestinationUrl = "https://cloud.oracle.com/database",
            CustomAlias = "oracle-pg-deploy",
            Title = "Oracle Cloud PostgreSQL",
            ExpiresAtUtc = unspecifiedExpiry
        }, userId: "owner-1", baseUrl: "https://clippy.io");

        Assert.True(created.Succeeded);
        Assert.NotNull(created.Data);
        Assert.NotNull(created.Data!.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, created.Data.ExpiresAtUtc!.Value.Kind);
        Assert.StartsWith("data:image/svg+xml;base64,", created.Data.QrCodeSvgBase64);

        // 2. Verify persisted entity in ApplicationDbContext has DateTimeKind.Utc
        await using (var db = dbFactory.CreateDbContext())
        {
            var stored = await db.ShortUrls.SingleAsync(x => x.Id == created.Data.Id);
            Assert.Equal(DateTimeKind.Utc, stored.CreatedAtUtc.Kind);
            Assert.NotNull(stored.ExpiresAtUtc);
            Assert.Equal(DateTimeKind.Utc, stored.ExpiresAtUtc!.Value.Kind);

            // Seed click telemetry with Unspecified DateTime to verify ApplicationDbContext.SaveChangesAsync normalizer
            db.UrlClickLogs.Add(new UrlClickLog
            {
                ShortUrlId = stored.Id,
                ShortCode = stored.shortCode,
                ClickedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                AnonymizedVisitorHash = "abc123hash",
                CountryCode = "PK",
                DeviceType = "Desktop",
                BrowserName = "Chrome",
                OsName = "Windows",
                ReferrerHost = "github.com"
            });
            await db.SaveChangesAsync();

            var savedLog = await db.UrlClickLogs.SingleAsync(x => x.ShortUrlId == stored.Id);
            Assert.Equal(DateTimeKind.Utc, savedLog.ClickedAtUtc.Kind);
        }

        // 3. Update with DateTimeKind.Local and verify cache eviction + UTC normalization
        var updated = await service.UpdateUrlAsync(created.Data.Id, new UpdateShortUrlRequest
        {
            DestinationUrl = "https://www.postgresql.org/docs/",
            CustomAlias = "pg-docs-updated",
            Title = "PostgreSQL Docs",
            ExpiresAtUtc = DateTime.Now.AddDays(30),
            IsActive = true
        }, userId: "owner-1", baseUrl: "https://clippy.io");

        Assert.True(updated.Succeeded);
        Assert.Equal(DateTimeKind.Utc, updated.Data!.ExpiresAtUtc!.Value.Kind);

        // 4. Pause & Resume toggle + L1 resolution check
        var paused = await service.ToggleUrlStatusAsync(created.Data.Id, "owner-1");
        Assert.True(paused.Succeeded);
        Assert.False(paused.Data);
        var resolvedWhenPaused = await resolutionCache.ResolveShortCodeAsync("pg-docs-updated");
        Assert.Equal(RedirectResolutionStatus.BlockedOrInactive, resolvedWhenPaused.Status);

        var resumed = await service.ToggleUrlStatusAsync(created.Data.Id, "owner-1");
        Assert.True(resumed.Succeeded);
        Assert.True(resumed.Data);
        var resolvedWhenActive = await resolutionCache.ResolveShortCodeAsync("pg-docs-updated");
        Assert.Equal(RedirectResolutionStatus.Found, resolvedWhenActive.Status);

        // 5. Analytics summary verification
        var analytics = await service.GetAnalyticsAsync("pg-docs-updated", "owner-1");
        Assert.NotNull(analytics);
        Assert.Equal(1, analytics!.TotalClicks);
        Assert.Equal(1, analytics.UniqueVisitors);
        Assert.Equal(7, analytics.Last7DaysClick.Count);
        Assert.Single(analytics.TopCountries);

        // 6. Delete + verify cache eviction
        var deleted = await service.DeleteUrlAsync(created.Data.Id, "owner-1");
        Assert.True(deleted);
        var resolvedAfterDelete = await resolutionCache.ResolveShortCodeAsync("pg-docs-updated");
        Assert.Equal(RedirectResolutionStatus.NotFound, resolvedAfterDelete.Status);
    }

    [Fact]
    public void PostgreSqlDesignTimeModel_HasValidMigrationsAndNoPendingModelChanges()
    {
        var factory = new DesignTimeApplicationDbContextFactory();
        using var db = factory.CreateDbContext([]);
        var migrations = db.Database.GetMigrations().ToList();
        Assert.NotEmpty(migrations);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class SimpleInMemoryFactory(string databaseName) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            return new ApplicationDbContext(options);
        }
    }
}
