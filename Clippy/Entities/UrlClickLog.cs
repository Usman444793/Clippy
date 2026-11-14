using System.ComponentModel.DataAnnotations;
namespace Clippy.Entities;
public class UrlClickLog
{
    public long Id { get; set; }
    public long ShortUrlId { get; set; }
    public ShortUrl ShortUrl { get; set; } = null!;
    [Required, MaxLength(16)]
    public string ShortCode { get; set; } = string.Empty;
    public DateTime ClickedAtUtc { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(64)]
    public string AnonymizedVisitorHash { get; set; } = string.Empty;
    [MaxLength(2)]
    public string CountryCode { get; set; } = "UN";
    [MaxLength(50)]
    public string DeviceType { get; set; } = "Desktop";
    [MaxLength(50)]
    public string BrowserName { get; set; } = "Unknown";
    [MaxLength(50)]
    public string OsName { get; set; } = "Unknown";
    [MaxLength(250)]
    public string ReferrerHost { get; set; } = "Direct";
}
