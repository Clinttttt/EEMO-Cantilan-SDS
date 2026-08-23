using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// The address of an LGU's seal, built from the request that asked for it.
///
/// <para>
/// Lifted out of <see cref="MunicipalitiesController"/> unchanged when the Head's activation page needed the same
/// address: an office setting its first password was shown StallTrack's own mark and nothing of its own. One copy,
/// because the part that is easy to get wrong is the scheme, and getting it wrong is silent — see below.
/// </para>
/// </summary>
public static class SealUrl
{
    /// <summary>
    /// Turns an EMBEDDED seal into the address of the seal endpoint; anything else is returned as it is.
    ///
    /// <para>
    /// A seal already recorded as a file path is left alone (the web host serves it), and a municipality with no seal
    /// on file gets null — never another municipality's mark. The address carries a version taken from the seal's own
    /// bytes, so it may be cached hard: a re-uploaded seal is a different address, an unchanged one is never fetched
    /// twice.
    /// </para>
    /// </summary>
    public static string? From(HttpRequest request, string? identifier, string? stored)
    {
        if (stored is not { Length: > 0 }) return stored;
        if (!stored.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return stored;
        if (string.IsNullOrWhiteSpace(identifier)) return stored;

        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored)))[..12].ToLowerInvariant();

        return $"{PublicScheme(request)}://{request.Host}{request.PathBase}" +
               $"/api/municipalities/{Uri.EscapeDataString(identifier)}/seal?v={version}";
    }

    /// <summary>
    /// The scheme the CALLER used, not the one that reached this process.
    ///
    /// <para>
    /// TLS terminates at the platform's proxy, so inside the container a request arrives over plain HTTP and
    /// <c>Request.Scheme</c> says "http". An http address for the seal, on a page served over https, is mixed content:
    /// the browser refuses to load it and the seal silently disappears. Caught before it shipped by reading the address
    /// the deployed API actually returned.
    /// </para>
    ///
    /// <para>
    /// Read from the forwarding header here rather than by turning on forwarded-headers processing for the whole
    /// application: that also rewrites the scheme every other component sees and the remote address the logs record,
    /// which is a wider change than one URL needs and would sit underneath the HTTPS redirect and HSTS.
    /// </para>
    /// </summary>
    public static string PublicScheme(HttpRequest request)
    {
        var forwarded = request.Headers["X-Forwarded-Proto"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // The header may carry a list, proxy by proxy; the first entry is the client's own.
            var first = forwarded.Split(',')[0].Trim();
            if (string.Equals(first, "https", StringComparison.OrdinalIgnoreCase)) return "https";
            if (string.Equals(first, "http", StringComparison.OrdinalIgnoreCase)) return "http";
        }

        return request.Scheme;
    }
}
