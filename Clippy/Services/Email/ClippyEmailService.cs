using System.Net;
using System.Text;
using Clippy.Data;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
namespace Clippy.Services.Email;
public sealed class ClippyEmailService(IOptions<EmailSettingsOptions> options,ILogger<ClippyEmailService> logger) : IClippyEmailService
{
    private readonly EmailSettingsOptions _settings = options.Value;
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var cleanUrl = WebUtility.HtmlDecode(confirmationLink);
        return SendTemplatedEmailAsync(
            toEmail: email,
            subject: "Verify your Clippy Enterprise account",
            category: "Account Verification",
            headline: "Verify your email address",
            introParagraph: $"Hi {user.DisplayName ?? email.Split('@')[0]}, welcome to Clippy Enterprise! Please confirm your email address by clicking the button below to activate your Snowflake link workspace, custom aliases, and real-time click analytics.",
            actionUrl: cleanUrl,
            actionLabel: "Verify Email Address",
            securityFooterNote: "If you did not create a Clippy Enterprise account, you can safely ignore this email."
        );
    }
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var cleanUrl = WebUtility.HtmlDecode(resetLink);
        return SendTemplatedEmailAsync(
            toEmail: email,
            subject: "Reset your Clippy Enterprise password",
            category: "Password Reset",
            headline: "Password reset request",
            introParagraph: $"Hi {user.DisplayName ?? email.Split('@')[0]}, we received a request to reset the password for your Clippy Enterprise account ({email}). Click the button below to set a new password.",
            actionUrl: cleanUrl,
            actionLabel: "Reset My Password",
            securityFooterNote: "This password reset link is single-use and expires shortly. If you did not request a password reset, no action is required."
        );
    }
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        return SendTemplatedEmailAsync(
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
        return SendTemplatedEmailAsync(
            toEmail: email,
            subject: subject,
            category: "Notification",
            headline: subject,
            introParagraph: htmlMessage
        );
    }
    public Task SendOtpVerificationEmailAsync(
        string toEmail,
        string otpCode,
        string purpose,
        DateTime expiresAtUtc,
        CancellationToken ct = default)
    {
        return SendTemplatedEmailAsync(
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
    public async Task SendTemplatedEmailAsync(
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
        var senderEmail = string.IsNullOrWhiteSpace(_settings.SenderEmail) || _settings.SenderEmail.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) || _settings.SenderEmail.StartsWith("EmailSettings__", StringComparison.OrdinalIgnoreCase) ? _settings.SmtpUsername : _settings.SenderEmail;
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost) || string.IsNullOrWhiteSpace(_settings.SmtpUsername) ||
        string.IsNullOrWhiteSpace(_settings.SmtpPassword) || _settings.SmtpUsername.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
        _settings.SmtpUsername.StartsWith("EmailSettings__", StringComparison.OrdinalIgnoreCase) || _settings.SmtpPassword.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
        _settings.SmtpPassword.StartsWith("EmailSettings__", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MailKit SMTP credentials are not configured. Please set 'EmailSettings:SenderEmail', 'EmailSettings:SmtpUsername', and 'EmailSettings:SmtpPassword' in .NET User Secrets.");
        }
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(_settings.SenderName) ? "Clippy Enterprise" : _settings.SenderName,senderEmail.Trim()));
        message.To.Add(MailboxAddress.Parse(toEmail.Trim()));
        message.Subject = subject;
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = BuildCourseraStyleEmailHtml(
                toEmail,
                category,
                headline,
                introParagraph,
                actionUrl,
                actionLabel,
                otpCode,
                securityFooterNote ?? "Sent by Clippy Enterprise Identity & Link Security."),
            TextBody = BuildPlainTextFallback(headline, introParagraph, actionUrl, otpCode, securityFooterNote)
        };
        message.Body = bodyBuilder.ToMessageBody();
        using var smtpClient = new SmtpClient();
        var secureSocketOptions = _settings.UseStartTls ? SecureSocketOptions.StartTls : (_settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto);
        try
        {
            await smtpClient.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureSocketOptions, ct);
            await smtpClient.AuthenticateAsync(_settings.SmtpUsername.Trim(), _settings.SmtpPassword, ct);
            await smtpClient.SendAsync(message, ct);
            await smtpClient.DisconnectAsync(true, ct);
            logger.LogInformation("[MailKit SMTP] Successfully sent '{Category}' email to {RecipientEmail} via {SmtpHost}:{SmtpPort}.", category, toEmail, _settings.SmtpHost, _settings.SmtpPort);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MailKit SMTP] Failed to send email to {RecipientEmail} via {SmtpHost}:{SmtpPort}.",
                toEmail, _settings.SmtpHost, _settings.SmtpPort);
            throw;
        }
    }
    private static string BuildPlainTextFallback(
        string headline,
        string introParagraph,
        string? actionUrl,
        string? otpCode,
        string? footerNote)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CLIPPY ENTERPRISE");
        sb.AppendLine("========================================");
        sb.AppendLine(headline);
        sb.AppendLine();
        sb.AppendLine(introParagraph);
        if (!string.IsNullOrWhiteSpace(otpCode))
        {
            sb.AppendLine();
            sb.AppendLine($"Verification Code: {otpCode}");
        }
        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            sb.AppendLine();
            sb.AppendLine($"Action Link: {actionUrl}");
        }
        sb.AppendLine();
        sb.AppendLine("----------------------------------------");
        sb.AppendLine(footerNote ?? "Sent by Clippy Enterprise.");
        return sb.ToString();
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
            <title>Clippy Enterprise</title>
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
