namespace Clippy.Extensions;
public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public string OtpHmacSecret { get; set; } = string.Empty;
    public int OtpExpirationMinutes { get; set; } = 5;
    public int MaxOtpAttempts { get; set; } = 3;
}
public sealed class ShortenerConfigOptions
{
    public const string SectionName = "ShortenerConfig";
    public string BaseDomain { get; set; } = string.Empty;
    public int SnowflakeMachineId { get; set; } = 1;
    public int DefaultCacheDurationMinutes { get; set; } = 30;
}