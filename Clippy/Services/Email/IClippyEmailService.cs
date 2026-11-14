using Clippy.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Clippy.Services.Email;

public sealed class EmailSettingsOptions
{
    public const string SectionName = "EmailSettings";

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string SenderName { get; set; } = "Clippy Enterprise";
    public string SenderEmail { get; set; } = string.Empty;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
}

public interface IClippyEmailService : IEmailSender<ApplicationUser>, IEmailSender
{
    Task SendOtpVerificationEmailAsync(
        string toEmail,
        string otpCode,
        string purpose,
        DateTime expiresAtUtc,
        CancellationToken ct = default);

    Task SendTemplatedEmailAsync(
        string toEmail,
        string subject,
        string category,
        string headline,
        string introParagraph,
        string? actionUrl = null,
        string? actionLabel = null,
        string? otpCode = null,
        string? securityFooterNote = null,
        CancellationToken ct = default);
}
