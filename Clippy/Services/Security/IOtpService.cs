namespace Clippy.Services.Security;

public record OtpGenerationResult(string rawCode, DateTime ExpiresAtUtc);
public record OtpValidationResult(bool IsValid, string Message, int RemainingAttempts);
public interface IOtpService
{
    OtpGenerationResult GenerateOtp(string userIdentifier, string purpose = "Auth");
    OtpValidationResult VerifyOtp(string userIdentifier, string inputCode, string purpose = "Auth");
}