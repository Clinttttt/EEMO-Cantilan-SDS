using System.Threading;

namespace EEMOCantilanSDS.Application.Common.Interface.Services
{
    /// <summary>
    /// Renders a QR code for a payload (used for the TOTP <c>otpauth://</c> provisioning URI so the user can
    /// scan it with an authenticator app instead of typing a 32-character key).
    /// </summary>
    public interface IQrCodeGenerator
    {
        /// <summary>
        /// Returns a self-contained <c>data:image/png;base64,...</c> URI, ready to drop into an
        /// <c>&lt;img src&gt;</c>. Returns an empty string if the payload is blank.
        /// </summary>
        string ToPngDataUri(string payload, int pixelsPerModule = 6);
    }
}
