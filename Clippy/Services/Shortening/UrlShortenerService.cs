using System.Text.RegularExpressions;
using Clippy.Client.Models;
using Clippy.Data;
using Clippy.Entities;
using Clippy.Services.Caching;
using Clippy.Services.QrCode;
using Clippy.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace Clippy.Services.Shortening;

public sealed class UrlShortenerService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IShortCodeGenerator idGenerator,
    IUrlSafetyScanner safetyScanner,
    IQrCodeService qrCodeService,
    IUrlResolutionCache cache) : IUrlShortenerService
{
    private readonly HashSet<string> ReservedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "auth", "account", "login", "register", "logout", "dashboard", "analytics",
        "admin", "error", "health", "healthz", "mailbox", "not-found", "signin-google", "signin-facebook", "signin-oidc"
    };

    public async Task<ServiceResult<ShortUrlDto>> CreateShortUrlAsync(CreateShortUrlRequest request, string? userId, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ServiceResult<ShortUrlDto>(false, null, "Authentication required: Please sign in to your Clippy account to shorten and own URLs.");
        }

        var scan = safetyScanner.InspectUrl(request.DestinationUrl, baseUrl);
        if (scan.Status == LinkSafetyStatus.FlaggedMalicious)
        {
            return new ServiceResult<ShortUrlDto>(false, null, scan.Reason ?? "URL blocked by security policy.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        string? cleanAlias = string.IsNullOrWhiteSpace(request.CustomAlias) ? null : request.CustomAlias.Trim();
        if (cleanAlias is not null)
        {
            if (cleanAlias.Length < 3 || cleanAlias.Length > 32 || !Regex.IsMatch(cleanAlias, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new ServiceResult<ShortUrlDto>(false, null, "Custom alias must be 3-32 characters and contain only letters, numbers, hyphens, and underscores.");
            }
            if (ReservedRoutes.Contains(cleanAlias))
            {
                return new ServiceResult<ShortUrlDto>(false, null, $"The alias '{cleanAlias}' is a reserved system route.");
            }
            var aliasExists = await db.ShortUrls.AnyAsync(x => x.CustomAlias == cleanAlias || x.shortCode == cleanAlias, ct);
            if (aliasExists)
            {
                return new ServiceResult<ShortUrlDto>(false, null, $"The custom alias '{cleanAlias}' is already taken.");
            }
        }

        var generated = idGenerator.NextId();
        var effectiveCode = cleanAlias ?? generated.ShortCode;
        var destinationUrl = request.DestinationUrl!.Trim();
        var entity = new ShortUrl
        {
            Id = generated.SnowflakeId,
            shortCode = generated.ShortCode,
            CustomAlias = cleanAlias,
            DestinationUrl = destinationUrl,
            Title = string.IsNullOrWhiteSpace(request.Title) ? ExtractHostTitle(destinationUrl) : request.Title.Trim(),
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = NormalizeToUtc(request.ExpiresAtUtc),
            IsActive = true,
            SafetyStatus = scan.Status,
            TotalClicks = 0
        };

        db.ShortUrls.Add(entity);
        await db.SaveChangesAsync(ct);
        cache.Evict(effectiveCode);
        return new ServiceResult<ShortUrlDto>(true, MapToDto(entity, baseUrl), null);
    }

    public async Task<ServiceResult<ShortUrlDto>> UpdateUrlAsync(long id, UpdateShortUrlRequest request, string userId, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ServiceResult<ShortUrlDto>(false, null, "Unauthorized: Only the authenticated link owner can modify this URL.");
        }

        var scan = safetyScanner.InspectUrl(request.DestinationUrl, baseUrl);
        if (scan.Status == LinkSafetyStatus.FlaggedMalicious)
        {
            return new ServiceResult<ShortUrlDto>(false, null, scan.Reason ?? "Updated destination URL blocked by security policy.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ShortUrls.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (entity is null)
        {
            return new ServiceResult<ShortUrlDto>(false, null, "Link not found or you do not have permission to modify it.");
        }

        string? cleanAlias = string.IsNullOrWhiteSpace(request.CustomAlias) ? null : request.CustomAlias.Trim();
        if (cleanAlias is not null)
        {
            if (cleanAlias.Length < 3 || cleanAlias.Length > 32 || !Regex.IsMatch(cleanAlias, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new ServiceResult<ShortUrlDto>(false, null, "Custom alias must be 3-32 characters and contain only letters, numbers, hyphens, and underscores.");
            }
            if (ReservedRoutes.Contains(cleanAlias))
            {
                return new ServiceResult<ShortUrlDto>(false, null, $"The alias '{cleanAlias}' is a reserved system route.");
            }
            var aliasConflict = await db.ShortUrls.AnyAsync(
                x => x.Id != id && (x.CustomAlias == cleanAlias || x.shortCode == cleanAlias), ct);
            if (aliasConflict)
            {
                return new ServiceResult<ShortUrlDto>(false, null, $"The custom alias '{cleanAlias}' is already in use by another link.");
            }
        }

        var oldAlias = entity.CustomAlias;
        var destinationUrl = request.DestinationUrl.Trim();

        entity.DestinationUrl = destinationUrl;
        entity.CustomAlias = cleanAlias;
        entity.Title = string.IsNullOrWhiteSpace(request.Title) ? ExtractHostTitle(destinationUrl) : request.Title.Trim();
        entity.ExpiresAtUtc = NormalizeToUtc(request.ExpiresAtUtc);
        entity.IsActive = request.IsActive;
        entity.SafetyStatus = scan.Status;

        await db.SaveChangesAsync(ct);

        cache.Evict(entity.shortCode);
        if (!string.IsNullOrWhiteSpace(oldAlias))
        {
            cache.Evict(oldAlias);
        }
        if (!string.IsNullOrWhiteSpace(cleanAlias))
        {
            cache.Evict(cleanAlias);
        }

        return new ServiceResult<ShortUrlDto>(true, MapToDto(entity, baseUrl), null);
    }

    public async Task<ServiceResult<bool>> ToggleUrlStatusAsync(long id, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ServiceResult<bool>(false, false, "Unauthorized.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ShortUrls.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (entity is null)
        {
            return new ServiceResult<bool>(false, false, "Link not found or access denied.");
        }

        entity.IsActive = !entity.IsActive;
        await db.SaveChangesAsync(ct);

        cache.Evict(entity.shortCode);
        if (!string.IsNullOrWhiteSpace(entity.CustomAlias))
        {
            cache.Evict(entity.CustomAlias);
        }

        return new ServiceResult<bool>(true, entity.IsActive, null);
    }

    public async Task<List<ShortUrlDto>> GetUserUrlsAsync(string userId, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.ShortUrls
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return list.Select(x => MapToDto(x, baseUrl)).ToList();
    }

    public async Task<List<ShortUrlDto>> GetRecentPublicUrlsAsync(int count, string baseUrl, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.ShortUrls
            .AsNoTracking()
            .Where(x => x.IsActive && x.SafetyStatus == LinkSafetyStatus.VerifiedSafe)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(count)
            .ToListAsync(ct);

        return list.Select(x => MapToDto(x, baseUrl)).ToList();
    }

    public async Task<UrlAnalyticsSummaryDto?> GetAnalyticsAsync(string shortCode, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var url = await db.ShortUrls
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.shortCode == shortCode || x.CustomAlias == shortCode) && x.UserId == userId, ct);
        if (url is null) return null;

        var logs = await db.UrlClickLogs.AsNoTracking().Where(x => x.ShortUrlId == url.Id).ToListAsync(ct);
        var totalClicks = Math.Max(url.TotalClicks, logs.Count);
        var uniqueVisitors = logs.Select(x => x.AnonymizedVisitorHash).Distinct().Count();
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);
        var dailyPoints = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = sevenDaysAgo.AddDays(offset);
            var count = logs.LongCount(l => l.ClickedAtUtc.Date == date);
            return new DailyClickPointDto(date.ToString("MMM dd"), count);
        }).ToList();
        var topCountries = BuildCategoryBreakdown(logs.Select(x => x.CountryCode), logs.Count);
        var devices = BuildCategoryBreakdown(logs.Select(x => x.DeviceType), logs.Count);
        var referrers = BuildCategoryBreakdown(logs.Select(x => x.ReferrerHost), logs.Count);
        return new UrlAnalyticsSummaryDto(
            url.CustomAlias ?? url.shortCode,
            url.DestinationUrl,
            totalClicks,
            uniqueVisitors,
            url.CreatedAtUtc,
            dailyPoints,
            topCountries,
            devices,
            referrers
        );
    }

    public async Task<bool> DeleteUrlAsync(long id, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ShortUrls.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (entity is null) return false;
        var code = entity.shortCode;
        var alias = entity.CustomAlias;
        db.ShortUrls.Remove(entity);
        await db.SaveChangesAsync(ct);
        cache.Evict(code);
        if (!string.IsNullOrWhiteSpace(alias)) cache.Evict(alias);
        return true;
    }

    private ShortUrlDto MapToDto(ShortUrl entity, string baseUrl)
    {
        var routeSegment = string.IsNullOrWhiteSpace(entity.CustomAlias) ? entity.shortCode : entity.CustomAlias;
        var fullShortUrl = $"{baseUrl.TrimEnd('/')}/{routeSegment}";
        var qrDateUri = qrCodeService.GenerateSvgDataUri(fullShortUrl);
        return new ShortUrlDto
        (
            entity.Id,
            entity.shortCode,
            entity.CustomAlias,
            fullShortUrl,
            entity.DestinationUrl,
            entity.Title,
            entity.CreatedAtUtc,
            entity.ExpiresAtUtc,
            entity.IsActive,
            entity.SafetyStatus.ToString(),
            entity.TotalClicks,
            qrDateUri
        );
    }

    private static List<CategoryMetricsDto> BuildCategoryBreakdown(IEnumerable<string> values, int total)
    {
        if (total == 0) return [];
        return values.GroupBy(v => string.IsNullOrWhiteSpace(v) ? "Unknown" : v).Select(g => new CategoryMetricsDto(g.Key,
        g.LongCount(), Math.Round((double)g.Count() / total * 100.0, 1))).OrderByDescending(x => x.Count).Take(5).ToList();
    }

    private static string ExtractHostTitle(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "Shortened Link";

    private static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Unspecified && dt.TimeOfDay == TimeSpan.Zero)
        {
            dt = dt.Date.AddDays(1).AddSeconds(-1);
        }

        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }
}
