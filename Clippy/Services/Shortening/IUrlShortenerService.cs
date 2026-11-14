using Clippy.Client.Models;
namespace Clippy.Services.Shortening;
public record ServiceResult<T>(bool Succeeded, T? Data, string? ErrorMessage = null);
public interface IUrlShortenerService
{
    Task<ServiceResult<ShortUrlDto>> CreateShortUrlAsync(CreateShortUrlRequest request,string? userId,string baseUrl,CancellationToken ct = default);
    Task<List<ShortUrlDto>> GetUserUrlsAsync(string userId,string baseUrl,CancellationToken ct = default);
    Task<List<ShortUrlDto>> GetRecentPublicUrlsAsync(int count, string baseUrl, CancellationToken ct = default);
    Task<UrlAnalyticsSummaryDto?> GetAnalyticsAsync(string shortCode, string? userId, CancellationToken ct = default);
    Task<bool> DeleteUrlAsync(long id, string userId, CancellationToken ct = default);
}