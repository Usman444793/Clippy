using System.Text;
using QRCoder;
namespace Clippy.Services.QrCode;
public class QrCodeService : IQrCodeService
{
    public string GenerateSvgDataUri(string url)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url,QRCodeGenerator.ECCLevel.M);
        var svgQrCode = new SvgQRCode(qrCodeData);
        var svgText = svgQrCode.GetGraphic(pixelsPerModule: 8, darkColorHex: "#115D15", lightColorHex: "#FFFFFF", drawQuietZones: true);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svgText));
        return $"data:image/svg+xml;base64,{base64}";
    }
}
