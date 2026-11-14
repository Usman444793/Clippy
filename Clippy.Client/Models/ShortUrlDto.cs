namespace Clippy.Client.Models;
public record ShortUrlDto
(
    long Id,
    string ShortCode,
    string? CustomAlias,
    string ShortenedFullUrl,
    string DestinationUrl,
    string? Title,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsActive,
    string SafetyStatus,
    long TotalClicks,
    string QrCodeSvgBase64
);