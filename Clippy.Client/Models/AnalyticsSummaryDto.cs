namespace Clippy.Client.Models;

public record DailyClickPointDto(string DateLabel, long Clicks);
public record CategoryMetricsDto(string Label,long Count,double Percentage);
public record UrlAnalyticsSummaryDto
(
    string ShortCode,
    string DestinationUrl,
    long TotalClicks,
    long UniqueVisitors,
    DateTime CreatedAtUtc,
    List<DailyClickPointDto> Last7DaysClick,
    List<CategoryMetricsDto> TopCountries,
    List<CategoryMetricsDto> DeviceBreakdown,
    List<CategoryMetricsDto> TopReferrers
);