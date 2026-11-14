namespace Clippy.Services.QrCode;
public interface IQrCodeService
{
    string GenerateSvgDataUri(string url);
}