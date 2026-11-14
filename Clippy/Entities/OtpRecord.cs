using System.ComponentModel.DataAnnotations;
namespace Clippy.Entities;
public class OtpRecord
{
    public long Id { get; set; }
    [Required, MaxLength(256)]
    public string TargetIdentifier { get; set; } = string.Empty;
    [Required, MaxLength(50)]
    public string Purpose { get; set; } = "Login2FA";
    [Required, MaxLength(128)]
    public string CodeHmac { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int AttemptCount { get; set; } = 0;
    public bool IsRevoked { get; set; } = false;
    [MaxLength(64)]
    public string? RequestIpHash { get; set; }
}
