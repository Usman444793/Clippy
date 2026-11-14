using Clippy.Entities;
namespace Clippy.Services.Caching;
public enum RedirectResolutionStatus
{
    Found = 1,
    NotFound = 2,
    Expired = 3,
    BlockedOrInactive = 4
}
public record ResolvedUrlResult(
    RedirectResolutionStatus Status,
    long ShortUrlId = 0,
    string? DestinationUrl = null,
    string? ShortCode = null
);
public interface IUrlResolutionCache
{
    Task<ResolvedUrlResult> ResolveShortCodeAsync(string codeOrAlias, CancellationToken cancellationToken = default);
    void Evict(string codeOrAlias);
}