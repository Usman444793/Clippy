using Clippy.Data;
using Microsoft.EntityFrameworkCore;
namespace Clippy.Services.Shortening;
public sealed class ExpiredUrlCleanupWorker(IDbContextFactory<ApplicationDbContext> dbFactory,ILogger<ExpiredUrlCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        logger.LogInformation("Clippy Expired Link & OTP Janitor started.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error occurred during scheduled link/OTP cleanup sweep.");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
    private async Task RunCleanupSweepAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var deactivatedLinks = await db.ShortUrls.Where(x => x.IsActive && x.ExpiresAtUtc != null && x.ExpiresAtUtc <= now).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
        var cutoff = now.AddHours(-24);
        var purgedOtps = await db.OtpRecords.Where(x => x.ExpiresAtUtc <= cutoff).ExecuteDeleteAsync(ct);
        if (deactivatedLinks > 0 || purgedOtps > 0)
        {
            logger.LogInformation("[Janitor Sweep] Deactivated {DeactivatedCount} expired links and purged {PurgedOtpCount} old OTP records.",deactivatedLinks, purgedOtps);
        }
    }
}