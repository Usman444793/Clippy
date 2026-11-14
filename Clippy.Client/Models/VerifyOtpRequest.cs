using System.ComponentModel.DataAnnotations;
namespace Clippy.Client.Models;

public class SendOtpRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = "Login2FA";
}
public class VerifyOtpRequest
{
    [Required, EmailAddress]
    public string EmailAddress { get; set; } = string.Empty;
    [Required, StringLength(6, MinimumLength = 6, ErrorMessage = "Enter the 6-digit OTP code.")]
    public string Code { get; set; } = string.Empty;
    public string Purpose { get; set; } = "Login2FA";
}
