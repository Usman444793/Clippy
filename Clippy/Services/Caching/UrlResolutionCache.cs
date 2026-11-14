using System.Collections.Concurrent;
using System.Data;
using Clippy.Data;
using Clippy.Entities;
using Clippy.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
namespace Clippy.Services.Caching;
public class UrlResolutionCache : IUrlResolutionCache
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<ApplicationDbContext> _dbfactory;
    private readonly ShortenerConfigOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.Ordinal);
    private sealed record CachedUrlEntry
    (
        long Id,
        string ShortCode,
        string DestinationUrl,
        bool IsActive,
        DateTime? ExpiresAtUtc,
        LinkSafetyStatus SafetyStatus,
        bool IsNegativeCache = false
    );
    public UrlResolutionCache(IMemoryCache cache,IDbContextFactory<ApplicationDbContext> dbfactory,IOptions<ShortenerConfigOptions> options)
    {
        _cache = cache;
        _dbfactory = dbfactory;
        _options = options.Value;
    }
    public async Task<ResolvedUrlResult> ResolveShortCodeAsync(string codeOrAlias, CancellationToken cancellationToken = default)
    {
        var normalizedKey = codeOrAlias.Trim();
        var cacheKey = $"clippy:resolve:{normalizedKey}";
        if (_cache.TryGetValue(cacheKey, out CachedUrlEntry? cached) && cached is not null)
        {
            return EvaluateEntry(cached);
        }
        var keyLock = _keyLocks.GetOrAdd(normalizedKey,_ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return EvaluateEntry(cached);
            }
            await using var db = await _dbfactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.ShortUrls.AsNoTracking().Where(x => x.shortCode == normalizedKey || x.CustomAlias == normalizedKey)
                .Select(x => new CachedUrlEntry(
                    x.Id,
                    x.shortCode,
                    x.DestinationUrl,
                    x.IsActive,
                    x.ExpiresAtUtc,
                    x.SafetyStatus,
                    false
                )).FirstOrDefaultAsync(cancellationToken);
            if (entity is null)
            {
                var negativeEntry = new CachedUrlEntry(0, normalizedKey, string.Empty, false, null, LinkSafetyStatus.VerifiedSafe, IsNegativeCache: true);
                _cache.Set(cacheKey, negativeEntry, TimeSpan.FromSeconds(30));
                return new ResolvedUrlResult(RedirectResolutionStatus.NotFound);
            }
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.DefaultCacheDurationMinutes),
                Priority = CacheItemPriority.High
            };
            _cache.Set(cacheKey, entity, cacheOptions);
            return EvaluateEntry(entity);
        }
        finally
        {
            keyLock.Release();
        }
    }
        public void Evict(string codeOrAlias)
        {
        if (string.IsNullOrWhiteSpace(codeOrAlias)) return;
        _cache.Remove($"clippy:resolve:{codeOrAlias.Trim()}");
        }
    private static ResolvedUrlResult EvaluateEntry(CachedUrlEntry entry)
    {
        if (entry.IsNegativeCache)
        {
            return new ResolvedUrlResult(RedirectResolutionStatus.NotFound);
        }
        if (!entry.IsActive || entry.SafetyStatus == LinkSafetyStatus.FlaggedMalicious)
        {
            return new ResolvedUrlResult(RedirectResolutionStatus.BlockedOrInactive);
        }
        if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return new ResolvedUrlResult(RedirectResolutionStatus.Expired);
        }
        return new ResolvedUrlResult(
            RedirectResolutionStatus.Found,
            entry.Id,
            entry.DestinationUrl,
            entry.ShortCode
        );
    }
}
