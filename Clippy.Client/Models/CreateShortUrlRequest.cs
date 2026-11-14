using System.ComponentModel.DataAnnotations;
namespace Clippy.Client.Models;
public class CreateShortUrlRequest
{
    [Required(ErrorMessage = "Please enter a destination URL to shorten")]
    [Url(ErrorMessage = "Please Enter a valid HTTP or HTTPS URL")]
    [MaxLength(2048)]
    public string? DestinationUrl { get; set; }
    [MaxLength(32,ErrorMessage = "Custom Alias cannot exceed 32 charecters")]
    [RegularExpression(@"^[a-zA-Z0-9\-_]*$",ErrorMessage = "Custom alias can only contain letters, numbers, hyphens, and underscores.")]
    public string? CustomAlias { get; set; }
    [MaxLength(200)]
    public string? Title { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
