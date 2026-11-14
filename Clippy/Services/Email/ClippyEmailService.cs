using System.Net;
using System.Net.Mail;
using System.Text;
using Clippy.Data;
using Microsoft.Extensions.Options;

namespace Clippy.Services.Email;

public sealed class ClippyEmailService : IClippyEmailService
{
    private readonly ILogger<ClippyEmailService> _logger;
    private readonly string _outboxDir;
    private readonly Lock _sync = new();
    private readonly List<DispatchedEmailMessage> _messages = [];
    private EmailSettingsOptions _runtimeSettings;

    public event Action? OnMailboxUpdated;

    public ClippyEmailService(
        IOptions<EmailSettingsOptions> options,
        IWebHostEnvironment env,
        ILogger<ClippyEmailService> logger)
    {
        _logger = logger;
        _runtimeSettings = new EmailSettingsOptions
        {
            SmtpHost = options.Value.SmtpHost,
            SmtpPort = options.Value.SmtpPort,
            SmtpUser = options.Value.SmtpUser,
            SmtpPassword = options.Value.SmtpPassword,
            EnableSsl = options.Value.EnableSsl,
            FromEmail = string.IsNullOrWhiteSpace(options.Value.FromEmail) ? "security@clippy.io" : options.Value.FromEmail,
            FromName = string.IsNullOrWhiteSpace(options.Value.FromName) ? "Clippy Enterprise Security" : options.Value.FromName
        };

        _outboxDir = Path.Combine(env.ContentRootPath, "App_Data", "EmailOutbox");
        try
        {
            Directory.CreateDirectory(_outboxDir);
        }
        catch
        {
            // Ignore directory creation errors in restricted environments
        }
    }

    public EmailSettingsOptions GetCurrentSettings()
    {
        lock (_sync)
        {
            return new EmailSettingsOptions
            {
                SmtpHost = _runtimeSettings.SmtpHost,
                SmtpPort = _runtimeSettings.SmtpPort,
                SmtpUser = _runtimeSettings.SmtpUser,
                SmtpPassword = _runtimeSettings.SmtpPassword,
                EnableSsl = _runtimeSettings.EnableSsl,
                FromEmail = _runtimeSettings.FromEmail,
                FromName = _runtimeSettings.FromName
            };
        }
    }

    public void UpdateRuntimeSmtpSettings(EmailSettingsOptions updated)
    {
        lock (_sync)
        {
            _runtimeSettings = new EmailSettingsOptions
            {
                SmtpHost = updated.SmtpHost?.Trim() ?? string.Empty,
                SmtpPort = updated.SmtpPort > 0 ? updated.SmtpPort : 587,
                SmtpUser = updated.SmtpUser?.Trim() ?? string.Empty,
                SmtpPassword = updated.SmtpPassword ?? string.Empty,
                EnableSsl = updated.EnableSsl,
                FromEmail = string.IsNullOrWhiteSpace(updated.FromEmail) ? "security@clippy.io" : updated.FromEmail.Trim(),
                FromName = string.IsNullOrWhiteSpace(updated.FromName) ? "Clippy Enterprise Security" : updated.FromName.Trim()
            };
        }
    }

    public IReadOnlyList<DispatchedEmailMessage> GetRecentEmails(string? filterEmail = null)
    {
        lock (_sync)
        {
            var query = _messages.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(filterEmail))
            {
                query = query.Where(x => x.ToEmail.Equals(filterEmail.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            return query.OrderByDescending(x => x.SentAtUtc).Take(50).ToList();
        }
    }

    public int GetUnreadCount(string? filterEmail = null)
    {
        lock (_sync)
        {
            var query = _messages.Where(x => !x.IsRead);
            if (!string.IsNullOrWhiteSpace(filterEmail))
            {
                query = query.Where(x => x.ToEmail.Equals(filterEmail.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            return query.Count();
        }
    }

    public void MarkAsRead(Guid id)
    {
        lock (_sync)
        {
            var idx = _messages.FindIndex(x => x.Id == id);
            if (idx >= 0 && !_messages[idx].IsRead)
            {
                _messages[idx] = _messages[idx] with { IsRead = true };
            }
        }
        OnMailboxUpdated?.Invoke();
    }

    public void MarkAllAsRead()
    {
        lock (_sync)
        {
            for (int i = 0; i < _messages.Count; i++)
            {
                if (!_messages[i].IsRead)
                {
                    _messages[i] = _messages[i] with { IsRead = true };
                }
            }
        }
        OnMailboxUpdated?.Invoke();
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var cleanUrl = WebUtility.HtmlDecode(confirmationLink);
        return SendCustomTemplateEmailAsync(
            toEmail: email,
            subject: "Verify your Clippy Enterprise account",
            category: "Account Verification",
            headline: "Verify your email address",
            introParagraph: $"Hi {user.DisplayName ?? email.Split('@')[0]}, welcome to Clippy Enterprise! Please confirm your email address so we can activate your personal Snowflake link workspace, custom aliases, and real-time click analytics.",
            actionUrl: cleanUrl,
            actionLabel: "Verify Email Address",
            securityFooterNote: "If you did not create a Clippy Enterprise account, you can safely ignore this message."
        );
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var cleanUrl = WebUtility.HtmlDecode(resetLink);
        return SendCustomTemplateEmailAsync(
            toEmail: email,
            subject: "Reset your Clippy Enterprise password",
            category: "Password Reset",
            headline: "Password reset request",
            introParagraph: $"Hi {user.DisplayName ?? email.Split('@')[0]}, we received a request to reset the password for your Clippy Enterprise account ({email}). Click the button below to choose a new password.",
            actionUrl: cleanUrl,
            actionLabel: "Reset My Password",
            securityFooterNote: "This password reset link is single-use and expires shortly. If you didn't request a password reset, your account remains secure."
        );
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        return SendCustomTemplateEmailAsync(
            toEmail: email,
            subject: $"Your Clippy password reset code: {resetCode}",
            category: "Security Code",
            headline: "Your password reset code",
            introParagraph: $"Hi {user.DisplayName ?? email.Split('@')[0]}, use the verification code below to reset your Clippy Enterprise account password.",
            otpCode: resetCode,
            securityFooterNote: "Never share this security code with anyone."
        );
    }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        return SendCustomTemplateEmailAsync(
            toEmail: email,
            subject: subject,
            category: "Notification",
            headline: subject,
            introParagraph: htmlMessage
        );
    }

    public Task<DispatchedEmailMessage> SendOtpVerificationEmailAsync(string toEmail, string otpCode, string purpose, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        return SendCustomTemplateEmailAsync(
            toEmail: toEmail,
            subject: $"{otpCode} is your Clippy Enterprise verification code",
            category: $"OTP ({purpose})",
            headline: "Your 6-digit verification code",
            introParagraph: $"Enter the following 6-digit cryptographic verification code to complete your {purpose} sign-in on Clippy Enterprise. This code expires at {expiresAtUtc:HH:mm:ss} UTC.",
            otpCode: otpCode,
            securityFooterNote: "Protected by HMAC-SHA256 & strict 3-attempt brute-force lockout.",
            ct: ct
        );
    }

    public async Task<DispatchedEmailMessage> SendCustomTemplateEmailAsync(
        string toEmail,
        string subject,
        string category,
        string headline,
        string introParagraph,
        string? actionUrl = null,
        string? actionLabel = null,
        string? otpCode = null,
        string? securityFooterNote = null,
        CancellationToken ct = default)
    {
        var settings = GetCurrentSettings();
        var htmlBody = BuildCourseraStyleEmailHtml(
            toEmail,
            category,
            headline,
            introParagraph,
            actionUrl,
            actionLabel,
            otpCode,
            securityFooterNote ?? "Sent by Clippy Enterprise Identity & Link Security.");

        bool deliveredSmtp = false;
        string deliveryNote;

        if (!string.IsNullOrWhiteSpace(settings.SmtpHost) &&
            !string.IsNullOrWhiteSpace(settings.SmtpUser) &&
            !string.IsNullOrWhiteSpace(settings.SmtpPassword))
        {
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(settings.FromEmail, settings.FromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };
                message.To.Add(new MailAddress(toEmail));

                using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
                {
                    EnableSsl = settings.EnableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(settings.SmtpUser, settings.SmtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 10000
                };

                await client.SendMailAsync(message, ct);
                deliveredSmtp = true;
                deliveryNote = $"Delivered via SMTP ({settings.SmtpHost}:{settings.SmtpPort}) & archived to Live Mailbox.";
                _logger.LogInformation("[CLIPPY EMAIL SMTP SENT] To={ToEmail} | Subject={Subject}", toEmail, subject);
            }
            catch (Exception ex)
            {
                deliveryNote = $"SMTP delivery failed ({ex.Message}); captured in Live Mailbox Outbox for immediate use.";
                _logger.LogWarning(ex, "[CLIPPY EMAIL SMTP FALLBACK] Captured in Live Outbox for {ToEmail}", toEmail);
            }
        }
        else
        {
            deliveryNote = "Captured in Clippy Live Mailbox Outbox (Configure SMTP in Mailbox settings for external delivery).";
        }

        var id = Guid.NewGuid();
        var dispatched = new DispatchedEmailMessage(
            Id: id,
            ToEmail: toEmail,
            Subject: subject,
            Category: category,
            HtmlBody: htmlBody,
            ActionUrl: actionUrl,
            ActionLabel: actionLabel,
            OtpCode: otpCode,
            SentAtUtc: DateTime.UtcNow,
            DeliveredViaSmtp: deliveredSmtp,
            DeliveryStatusNote: deliveryNote,
            IsRead: false
        );

        try
        {
            var filePath = Path.Combine(_outboxDir, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{id:N}.html");
            await File.WriteAllTextAsync(filePath, htmlBody, Encoding.UTF8, ct);
        }
        catch
        {
            // Ignore disk write issues if sandbox restricts folder
        }

        lock (_sync)
        {
            _messages.Add(dispatched);
            if (_messages.Count > 100)
            {
                _messages.RemoveAt(0);
            }
        }

        OnMailboxUpdated?.Invoke();
        return dispatched;
    }

    private static string BuildCourseraStyleEmailHtml(
        string recipientEmail,
        string category,
        string headline,
        string introParagraph,
        string? actionUrl,
        string? actionLabel,
        string? otpCode,
        string footerNote)
    {
        var sb = new StringBuilder();
        sb.Append("""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Clippy Enterprise Notification</title>
        </head>
        <body style="margin:0;padding:0;background-color:#F7F3EE;font-family:'Poppins',-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;color:#14181F;">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#F7F3EE;padding:32px 16px;">
                <tr>
                    <td align="center">
                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:580px;background-color:#FFFFFF;border-radius:24px;overflow:hidden;border:1px solid #E6E1DA;box-shadow:0 16px 40px rgba(13,16,23,0.06);">
                            <!-- Header -->
                            <tr>
                                <td style="background-color:#0D1017;padding:26px 32px;">
                                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                                        <tr>
                                            <td>
                                                <span style="display:inline-block;background-color:#73D167;color:#0D1017;font-weight:800;font-size:14px;width:28px;height:28px;line-height:28px;text-align:center;border-radius:50%;margin-right:8px;">&#128206;</span>
                                                <span style="font-size:20px;font-weight:800;color:#FFFFFF;letter-spacing:-0.02em;">clippy<span style="color:#73D167;text-decoration:underline;">.io</span></span>
                                            </td>
                                            <td align="right">
                                                <span style="background-color:rgba(115,209,103,0.18);color:#73D167;font-size:11px;font-weight:700;padding:5px 12px;border-radius:9999px;text-transform:uppercase;letter-spacing:0.04em;">
        """);
        sb.Append(WebUtility.HtmlEncode(category));
        sb.Append("""
                                                </span>
                                            </td>
                                        </tr>
                                    </table>
                                </td>
                            </tr>
                            <!-- Body -->
                            <tr>
                                <td style="padding:36px 32px 28px 32px;">
                                    <h1 style="margin:0 0 14px 0;font-size:24px;font-weight:800;color:#0D1017;line-height:1.25;">
        """);
        sb.Append(WebUtility.HtmlEncode(headline));
        sb.Append("""
                                    </h1>
                                    <p style="margin:0 0 24px 0;font-size:15px;line-height:1.65;color:#4B5563;">
        """);
        sb.Append(introParagraph);
        sb.Append("""
                                    </p>
        """);

        if (!string.IsNullOrWhiteSpace(otpCode))
        {
            sb.Append("""
                                    <div style="background-color:#F7F3EE;border:2px dashed #73D167;border-radius:16px;padding:24px;text-align:center;margin:24px 0;">
                                        <div style="font-size:11px;font-weight:700;text-transform:uppercase;color:#276E1F;letter-spacing:0.08em;margin-bottom:8px;">
                                            One-Time Verification Code
                                        </div>
                                        <div style="font-family:'JetBrains Mono',monospace;font-size:34px;font-weight:800;letter-spacing:0.28em;color:#0D1017;">
            """);
            sb.Append(WebUtility.HtmlEncode(otpCode));
            sb.Append("""
                                        </div>
                                        <div style="font-size:12px;color:#68707E;margin-top:8px;">
                                            Valid for 5 minutes &bull; Single-use cryptographic token
                                        </div>
                                    </div>
            """);
        }

        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            sb.Append("""
                                    <div style="margin:28px 0;text-align:center;">
                                        <a href="
            """);
            sb.Append(actionUrl);
            sb.Append("""
            " style="display:inline-block;background-color:#73D167;color:#0D1017;font-weight:800;font-size:15px;text-decoration:none;padding:14px 32px;border-radius:9999px;box-shadow:0 8px 20px rgba(115,209,103,0.3);">
            """);
            sb.Append(WebUtility.HtmlEncode(actionLabel ?? "Continue to Clippy"));
            sb.Append("""
                                        </a>
                                    </div>
                                    <div style="background-color:#F7F3EE;border-radius:12px;padding:14px 16px;margin-top:20px;">
                                        <div style="font-size:11px;font-weight:700;color:#68707E;text-transform:uppercase;margin-bottom:4px;">
                                            Or copy and paste this direct link into your browser:
                                        </div>
                                        <div style="font-family:monospace;font-size:12px;color:#0D1017;word-break:break-all;">
            """);
            sb.Append(WebUtility.HtmlEncode(actionUrl));
            sb.Append("""
                                        </div>
                                    </div>
            """);
        }

        sb.Append("""
                                    <hr style="border:none;border-top:1px solid #E6E1DA;margin:28px 0 18px 0;" />
                                    <p style="margin:0;font-size:12px;line-height:1.6;color:#68707E;">
        """);
        sb.Append(WebUtility.HtmlEncode(footerNote));
        sb.Append("""
                                    </p>
                                </td>
                            </tr>
                            <!-- Footer -->
                            <tr>
                                <td style="background-color:#EFE9E1;padding:18px 32px;font-size:12px;color:#68707E;">
                                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                                        <tr>
                                            <td>
                                                Sent to <strong>
        """);
        sb.Append(WebUtility.HtmlEncode(recipientEmail));
        sb.Append("""
                                                </strong>
                                            </td>
                                            <td align="right">
        """);
        sb.Append($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.Append("""
                                            </td>
                                        </tr>
                                    </table>
                                </td>
                            </tr>
                        </table>
                    </td>
                </tr>
            </table>
        </body>
        </html>
        """);
        return sb.ToString();
    }
}
