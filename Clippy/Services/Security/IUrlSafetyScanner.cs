using Clippy.Entities;
namespace Clippy.Services.Security;
public record UrlScanResult(LinkSafetyStatus Status, string? Reason);
public interface IUrlSafetyScanner
{
    UrlScanResult InspectUrl(string? destinationUrl, string? BaseDomain = null);
}
