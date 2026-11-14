using System.Security.Cryptography;
using System.Text;
using Clippy.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
namespace Clippy.Services.Security;

public sealed class OtpService : IOtpService
{
    private readonly IMemoryCache _cache;
    private readonly SecurityOptions _options;
    private readonly ILogger<OtpService> _logger;
    private sealed class CachedOtpState
    {
        public required string HashedCode { get; init; }
        public int FailedAttempts { get; set; }
        public DateTime ExpiresAtUtc { get; init; }
    }
    public OtpService(IMemoryCache cache, IOptions<SecurityOptions> options, ILogger<OtpService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }
    public OtpGenerationResult GenerateOtp(string userIdentifier, string purpose = "Auth")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentifier);
        var rawCode = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        var cacheKey = BuildCacheKey(userIdentifier, purpose);
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.OtpExpirationMinutes);
        var state = new CachedOtpState
        {
            HashedCode = ComputeHmac(rawCode, userIdentifier, purpose),
            FailedAttempts = 0,
            ExpiresAtUtc = expiresAt
        };
        _cache.Set(cacheKey, state, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = expiresAt,
            Priority = CacheItemPriority.High
        });
        _logger.LogInformation("Generated OTP for {Identifier} ({Purpose}), expires at {ExpiresAt}", userIdentifier, purpose, expiresAt);
        return new OtpGenerationResult(rawCode, expiresAt);
    }
    public OtpValidationResult VerifyOtp(string userIdentifier, string inputCode, string purpose = "Auth")
    {
        if (string.IsNullOrWhiteSpace(userIdentifier) || string.IsNullOrWhiteSpace(inputCode))
        {
            return new OtpValidationResult(false, "Identifier and OTP code are required.", 0);
        }
        var cachedKey = BuildCacheKey(userIdentifier, purpose);
        if (!_cache.TryGetValue(cachedKey, out CachedOtpState? state) || state is null)
        {
            return new OtpValidationResult(false, "OTP code has expired or does not exist.", 0);
        }
        var expectedHash = ComputeHmac(inputCode.Trim(), userIdentifier, purpose);
        var isMatch = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(state.HashedCode), Convert.FromHexString(expectedHash));
        if (isMatch)
        {
            _cache.Remove(cachedKey);
            return new OtpValidationResult(true, "OTP validation successful.", _options.MaxOtpAttempts);
        }
        state.FailedAttempts++;
        if (state.FailedAttempts >= _options.MaxOtpAttempts)
        {
            _cache.Remove(cachedKey);
            _logger.LogWarning("OTP invalidated for {Identifier} after {MaxAttempts} failed attempts.", userIdentifier, _options.MaxOtpAttempts);
            return new OtpValidationResult(false, "Maximum OTP attempts exceeded. Please request a new OTP.", 0);
        }
        var remaining = Math.Max(0, _options.MaxOtpAttempts - state.FailedAttempts);
        return new OtpValidationResult(false, "Invalid verification code.", remaining);
    }
    public string ComputeHmac(string code, string userIdentifier, string purpose)
    {
        var secret = string.IsNullOrWhiteSpace(_options.OtpHmacSecret) ? "ClippyFallbackDevelopmentSecretKey_MustBe32Bytes!" : _options.OtpHmacSecret;
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes($"{userIdentifier.Trim().ToLowerInvariant()}:{purpose}:{code}");
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hashBytes);
    }
    private static string BuildCacheKey(string userIdentifier, string purpose) =>
        $"Clippy:Otp:{purpose.ToLowerInvariant()}:{userIdentifier.Trim().ToLowerInvariant()}";
}
