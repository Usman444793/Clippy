using System.ComponentModel.DataAnnotations;
using Clippy.Data;
namespace Clippy.Entities;
public enum LinkSafetyStatus
{
    PendingScan = 0,
    VerifiedSafe = 1,
    FlaggedMalicious = 2
}
public class ShortUrl
{
    public long Id { get; set; }
    [Required, MaxLength(16)]
    public string shortCode { get; set; } = string.Empty;
    [MaxLength(64)]
    public string? CustomAlias { get; set; }
    [Required, MaxLength(2048)]
    public string DestinationUrl { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? Title { get; set; }
    [MaxLength(450)]
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(256)]
    public string? PasswordHash { get; set; }
    public LinkSafetyStatus SafetyStatus { get; set; } = LinkSafetyStatus.VerifiedSafe;
    [MaxLength(300)]
    public string? FlaggedReason { get; set; }
    public long TotalClicks { get; set; }
    [Timestamp]
    public byte[]? RowVersion { get; set; }
    public ICollection<UrlClickLog> ClickLogs { get; set; } = new List<UrlClickLog>();
}