using System.Security.Cryptography;
using System.Text;
using Clippy.Client.Models;
using Clippy.Data;
using Clippy.Entities;
using Clippy.Extensions;
using Clippy.Services.Email;
using Clippy.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clippy.EndPoints;

public static class AuthApiEndpoints
{
    public static IEndpointRouteBuilder MapClippyAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/otp/send", async (
            [FromBody] SendOtpRequest request,
            HttpContext httpContext,
            IOtpService otpService,
            IClippyEmailService emailService,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest(new { error = "Valid email address is required." });
            }

            var cleanEmail = request.Email.Trim();
            var result = otpService.GenerateOtp(cleanEmail, request.Purpose);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ipHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown")))[..32];
            db.OtpRecords.Add(new OtpRecord
            {
                TargetIdentifier = cleanEmail.ToLowerInvariant(),
                Purpose = request.Purpose,
                CodeHmac = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.rawCode))),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = result.ExpiresAtUtc,
                RequestIpHash = ipHash
            });
            await db.SaveChangesAsync(ct);

            try
            {
                await emailService.SendOtpVerificationEmailAsync(
                    cleanEmail,
                    result.rawCode,
                    request.Purpose,
                    result.ExpiresAtUtc,
                    ct);

                return Results.Ok(new
                {
                    message = $"A 6-digit verification code has been sent via MailKit SMTP to {cleanEmail}.",
                    expiresAtUtc = result.ExpiresAtUtc
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "MailKit SMTP Delivery Failed",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).RequireRateLimiting(RateLimitExtensions.OtpPolicy).AllowAnonymous();

        group.MapPost("/otp/verify", async ([FromBody] VerifyOtpRequest request, IOtpService otpService, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
        {
            var verification = otpService.VerifyOtp(request.EmailAddress, request.Code, request.Purpose);
            if (!verification.IsValid)
            {
                return Results.BadRequest(new
                {
                    isValid = false,
                    message = verification.Message,
                    remainingAttempts = verification.RemainingAttempts
                });
            }
            var email = request.EmailAddress.Trim();
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = email.Split('@')[0],
                    AuthType = "OTP-Passwordless",
                    IsTwoFactorOtpEnabled = true
                };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return Results.BadRequest(new
                    {
                        isValid = false,
                        message = string.Join(" ", createResult.Errors.Select(e => e.Description))
                    });
                }
            }
            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new
            {
                isValid = true,
                message = "OTP Verified. You are signed in.",
                redirectUrl = "/dashboard"
            });
        }).RequireRateLimiting(RateLimitExtensions.OtpPolicy).AllowAnonymous();

        group.MapGet("/external/{provider}", (string provider, string? returnUrl, SignInManager<ApplicationUser> signInManager) =>
        {
            var authenticationScheme = provider.ToLowerInvariant() switch
            {
                "google" => "Google",
                "facebook" or "meta" => "Facebook",
                _ => provider
            };
            var redirectUrl = $"/api/auth/external-callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/dashboard")}";
            var properties = signInManager.ConfigureExternalAuthenticationProperties(authenticationScheme, redirectUrl);
            return Results.Challenge(properties, [authenticationScheme]);
        }).AllowAnonymous();

        group.MapGet("/external-callback", async (string? returnUrl, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) =>
        {
            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info is null)
            {
                return Results.Redirect("/Account/Login?error=ExternalLoginFailed");
            }
            var signInResult = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
            if (signInResult.Succeeded)
            {
                return Results.Redirect(returnUrl ?? "/dashboard");
            }
            var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? $"{info.ProviderKey}@{info.LoginProvider.ToLowerInvariant()}.clippy.local";
            var name = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? email.Split('@')[0];
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = name,
                    AuthType = info.LoginProvider
                };
                await userManager.CreateAsync(user);
            }
            await userManager.AddLoginAsync(user, info);
            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Redirect(returnUrl ?? "/dashboard");
        }).AllowAnonymous();

        return app;
    }
}
