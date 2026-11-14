using Clippy.Data;
using Clippy.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
namespace Clippy.Services.Analytics;
public class ClickAnalyticsBackgroundWorker(IClickTelemetryQueue queue, IDbContextFactory<ApplicationDbContext> dbfactory, ILogger<ClickAnalyticsBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        logger.LogInformation("Clippy Click Analytics Background Worker Started");
        var batch = new List<clickTelemetryEvent>(capacity: 100);
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await foreach (var item in queue.ReadAllAsync(timeoutCts.Token))
                    {
                        batch.Add(item);
                        if (batch.Count >= 100) break;
                    }
                }
                catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
                {
                }
                if (batch.Count > 0)
                {
                    await FlushBatchToDatabaseAsync(batch, stopToken);
                    batch.Clear();
                }
            }
            catch (Exception ex) when (!stopToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error flushing click analytics batch.");
                batch.Clear();
                await Task.Delay(1000, stopToken);
            }
        }
    }
    private async Task FlushBatchToDatabaseAsync(List<clickTelemetryEvent> events, CancellationToken cancellationToken)
    {
        await using var db = await dbfactory.CreateDbContextAsync(cancellationToken);
        var dailySalt = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var logsToInsert = new List<UrlClickLog>(events.Count);
        foreach (var ev in events)
        {
            var (device, browser, os) = ParseUserAgent(ev.UserAgent);
            var referrerHost = ExtractReferrerHost(ev.ReferHeader);
            var country = NormalizeCountry(ev.CountryHeader);
            var visitorHash = AnonymizeIp(ev.RawClientIp, dailySalt);
            logsToInsert.Add(new UrlClickLog
            {
                ShortUrlId = ev.ShortIdUrl,
                ShortCode = ev.ShortCode,
                ClickedAtUtc = ev.ClickAtUtc,
                AnonymizedVisitorHash = visitorHash,
                CountryCode = country,
                DeviceType = device,
                BrowserName = browser,
                OsName = os,
                ReferrerHost = referrerHost
            });
        }
        await db.UrlClickLogs.AddRangeAsync(logsToInsert, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var groupedCounts = events.GroupBy(x => x.ShortIdUrl).Select(g => new { ShortUrlId = g.Key, Count = (long)g.Count() });
        foreach (var group in groupedCounts)
        {
            await db.ShortUrls
                .Where(u => u.Id == group.ShortUrlId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotalClicks, u => u.TotalClicks + group.Count), cancellationToken);
        }
    }
    private static string AnonymizeIp(string Ip, string dailySalt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{Ip}:{dailySalt}:ClippyPrivacySalt"));
        return Convert.ToHexString(bytes)[..32];
    }
    private static string NormalizeCountry(string? countryHeader)
    {
        if (!string.IsNullOrWhiteSpace(countryHeader) && countryHeader.Trim().Length == 2)
        {
            return countryHeader.Trim().ToUpperInvariant();
        }
        return "PK";
    }
    private static string ExtractReferrerHost(string? referer)
    {
        if (string.IsNullOrWhiteSpace(referer)) return "Direct";
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        }
        return "Direct";
    }
    private static (string Device, string Browser, string Os) ParseUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return ("Desktop", "Unknown", "Unknown");
        var lower = ua.ToLowerInvariant();
        var device = "Desktop";
        if (lower.Contains("bot") || lower.Contains("spider") || lower.Contains("crawler")) device = "Bot";
        else if (lower.Contains("ipad") || lower.Contains("tablet")) device = "Tablet";
        else if (lower.Contains("mobi") || lower.Contains("android") || lower.Contains("iphone")) device = "Mobile";
        var browser = "Other";
        if (lower.Contains("edg/")) browser = "Edge";
        else if (lower.Contains("opr/") || lower.Contains("opera")) browser = "Opera";
        else if (lower.Contains("chrome/") && !lower.Contains("edg/")) browser = "Chrome";
        else if (lower.Contains("Firefox/".ToLowerInvariant())) browser = "Firefox";
        else if (lower.Contains("safari/") && !lower.Contains("chrome/")) browser = "Safari";
        var os = "Other";
        if (lower.Contains("windows")) os = "Windows";
        else if (lower.Contains("android")) os = "Android";
        else if (lower.Contains("iphone") || lower.Contains("ipad") || lower.Contains("ios")) os = "iOS";
        else if (lower.Contains("mac os") || lower.Contains("macintosh")) os = "macOS";
        else if (lower.Contains("linux")) os = "Linux";
        return (device, browser, os);
    }
}