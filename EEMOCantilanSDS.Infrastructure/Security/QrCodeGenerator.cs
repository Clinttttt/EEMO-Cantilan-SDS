using System;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using QRCoder;

namespace EEMOCantilanSDS.Infrastructure.Security
{
    /// <summary>
    /// QR rendering via QRCoder's <see cref="PngByteQRCode"/>, which is fully managed (no System.Drawing /
    /// native image dependency), so it works on the Linux App Service containers.
    /// </summary>
    public sealed class QrCodeGenerator : IQrCodeGenerator
    {
        public string ToPngDataUri(string payload, int pixelsPerModule = 6)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            // Q-level correction keeps the code scannable even if partially obscured on screen.
            using var generator = new QRCodeGenerator();
            var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
            return $"data:image/png;base64,{Convert.ToBase64String(png)}";
        }
    }
}
