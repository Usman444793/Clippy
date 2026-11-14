using System.Net;
using Clippy.Entities;
namespace Clippy.Services.Security;
public sealed class UrlSafetyScanner : IUrlSafetyScanner
{
    private static readonly HashSet<string> BlockedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "localhost","127.0.0.1","0.0.0.0","::1","bit.ly","tinyurl.com","t.co"
    };
    private static readonly string[] SuspiciousKeywords =
        ["free-crypto-airdrop","verify-wallet-seed","login-bank-update","stealer",".exe",".scr",".bat"];
    public UrlScanResult InspectUrl(string? destinationUrl, string? baseDomain = null)
    {
        if (string.IsNullOrWhiteSpace(destinationUrl))
        {
            return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, "Destination Url cannot be empty");
        }
        if (!Uri.TryCreate(destinationUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, "Malformed URL format");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, "Only Http and Https protocols are allowed");
        }
        if (!string.IsNullOrWhiteSpace(baseDomain) && Uri.TryCreate(baseDomain, UriKind.Absolute, out var appUri) &&
            string.Equals(uri.Host, appUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, "Cannot shorten a URL that already points to Clippy");
        }
        if (BlockedHosts.Contains(uri.Host))
        {
            return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, $"Host {uri.Host} is restricted due to Security reasons");
        }
        if (IPAddress.TryParse(uri.Host, out var iPAddress) && IPAddress.IsLoopback(iPAddress))
        {
            return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, "LoopBack Ip Address are not allowed");
        }
        var fullPath = uri.ToString().ToLowerInvariant();
        foreach (var keyword in SuspiciousKeywords)
        {
            if (fullPath.Contains(keyword))
            {
                return new UrlScanResult(LinkSafetyStatus.FlaggedMalicious, $"URL triggered threat heuristic rule ({keyword}).");
            }
        }
        return new UrlScanResult(LinkSafetyStatus.VerifiedSafe, null);
    }
}
