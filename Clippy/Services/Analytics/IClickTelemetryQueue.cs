namespace Clippy.Services.Analytics;

public record clickTelemetryEvent(
    long ShortIdUrl,
    string ShortCode,
    DateTime ClickAtUtc,
    string RawClientIp,
    string? UserAgent,
    string ReferHeader,
    string? CountryHeader
);
public interface IClickTelemetryQueue
{
    void EnqueueClick(clickTelemetryEvent telemetryEvent);
    IAsyncEnumerable<clickTelemetryEvent> ReadAllAsync(CancellationToken cancellationToken);
}