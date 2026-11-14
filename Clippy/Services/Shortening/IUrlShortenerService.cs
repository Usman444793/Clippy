using System.ComponentModel.DataAnnotations;
using Clippy.Client.Models;

namespace Clippy.Services.Shortening;

public record ServiceResult<T>(bool Succeeded, T? Data, string? ErrorMessage = null);

public sealed class UpdateShortUrlRequest
{
    [Required(ErrorMessage = "Destination URL is required.")]
    [Url(ErrorMessage = "Please enter a valid HTTP or HTTPS URL.")]
    [MaxLength(2048)]
    public string DestinationUrl { get; set; } = string.Empty;

    [MaxLength(32, ErrorMessage = "Custom alias cannot exceed 32 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9\-_]*$", ErrorMessage = "Custom alias can only contain letters, numbers, hyphens, and underscores.")]
    public string? CustomAlias { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}

public interface IUrlShortenerService
{
    Task<ServiceResult<ShortUrlDto>> CreateShortUrlAsync(CreateShortUrlRequest request, string? userId, string baseUrl, CancellationToken ct = default);
    Task<ServiceResult<ShortUrlDto>> UpdateUrlAsync(long id, UpdateShortUrlRequest request, string userId, string baseUrl, CancellationToken ct = default);
    Task<ServiceResult<bool>> ToggleUrlStatusAsync(long id, string userId, CancellationToken ct = default);
    Task<List<ShortUrlDto>> GetUserUrlsAsync(string userId, string baseUrl, CancellationToken ct = default);
    Task<List<ShortUrlDto>> GetRecentPublicUrlsAsync(int count, string baseUrl, CancellationToken ct = default);
    Task<UrlAnalyticsSummaryDto?> GetAnalyticsAsync(string shortCode, string? userId, CancellationToken ct = default);
    Task<bool> DeleteUrlAsync(long id, string userId, CancellationToken ct = default);
}
