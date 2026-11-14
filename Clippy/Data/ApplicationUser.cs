using Clippy.Entities;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
namespace Clippy.Data;
public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }
    [MaxLength(50)]
    public string AuthType { get; set; } = "Local";
    public bool IsTwoFactorOtpEnabled { get; set; }
    public int MonthlyLinkedQuota { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ShortUrl> ShortUrls { get; set; } = new List<ShortUrl>();
}