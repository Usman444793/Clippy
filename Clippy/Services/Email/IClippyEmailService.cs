using Clippy.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Clippy.Services.Email;

public sealed class EmailSettingsOptions
{
    public const string SectionName = "EmailSettings";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromEmail { get; set; } = "security@clippy.io";
    public string FromName { get; set; } = "Clippy Enterprise Security";
}

public sealed record DispatchedEmailMessage(
    Guid Id,
    string ToEmail,
    string Subject,
    string Category,
    string HtmlBody,
    string? ActionUrl,
    string? ActionLabel,
    string? OtpCode,
    DateTime SentAtUtc,
    bool DeliveredViaSmtp,
    string DeliveryStatusNote,
    bool IsRead = false
);

public interface IClippyEmailService : IEmailSender<ApplicationUser>, IEmailSender
{
    event Action? OnMailboxUpdated;
    IReadOnlyList<DispatchedEmailMessage> GetRecentEmails(string? filterEmail = null);
    int GetUnreadCount(string? filterEmail = null);
    void MarkAsRead(Guid id);
    void MarkAllAsRead();
    EmailSettingsOptions GetCurrentSettings();
    void UpdateRuntimeSmtpSettings(EmailSettingsOptions updated);
    Task<DispatchedEmailMessage> SendOtpVerificationEmailAsync(string toEmail, string otpCode, string purpose, DateTime expiresAtUtc, CancellationToken ct = default);
    Task<DispatchedEmailMessage> SendCustomTemplateEmailAsync(
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
