using System.Security.Cryptography;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalitySeal
{
    /// <summary>
    /// Reads an LGU's stored seal into an image that can be served.
    ///
    /// <para>
    /// A seal is recorded on the municipality's row as a base64 data URI. Its own type, and its own bytes, are what a
    /// response needs. The value is supplied by an office and the type ends up in a Content-Type header, so this
    /// accepts an image and nothing else, and refuses anything that could carry a second header or a parameter.
    /// </para>
    /// </summary>
    public static class SealDataUri
    {
        /// <summary>
        /// The seal's bytes and type, or null when the stored value is not an embedded image. An office whose seal is
        /// recorded as a file path is already served by the web host, so there is nothing to decode.
        /// </summary>
        public static MunicipalitySealDto? Decode(string? stored)
        {
            const string marker = ";base64,";
            if (stored is null || !stored.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;

            var markerAt = stored.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerAt <= "data:".Length) return null;

            var contentType = stored["data:".Length..markerAt];
            var payload = stored[(markerAt + marker.Length)..];
            if (contentType.Length == 0 || payload.Length == 0) return null;

            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || contentType.Any(c => c is '\r' or '\n' or ';' or ',' or ' '))
                return null;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(payload);
            }
            catch (FormatException)
            {
                return null;
            }

            if (bytes.Length == 0) return null;

            // The seal's own content decides its ETag, so a re-uploaded seal is a different address and a browser that
            // cached the old one asks again.
            var etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant() + "\"";
            return new MunicipalitySealDto(bytes, contentType, etag);
        }
    }
}
