using System;
using System.Collections.Generic;
using System.Text;

namespace EEMOCantilanSDS.Application.Common.Security;

/// <summary>
/// Keeps one-time credentials out of logs. An activation, invitation, password-reset, e-mail-verification, onboarding or
/// device-bind link carries its secret IN THE PATH, so any log line that prints a request path or URL would print a
/// credential that lets someone take over an account or an onboarding. Callers pass what they were about to log and log
/// the result instead; method, route shape, status and timing stay available for diagnosis.
/// </summary>
/// <remarks>
/// This is a log-time filter, not the control that protects the credential: tokens are stored only as hashes, expire and
/// work once. It exists so that a log file, a log shipper or a support screenshot is never a second place to steal one.
/// </remarks>
public static class SensitiveRoutes
{
    public const string Redacted = "[redacted]";

    /// <summary>Path prefixes whose NEXT segment is a credential. Compared case-insensitively on the path only.</summary>
    private static readonly string[] SecretFollows =
    [
        "/api/activation/context/",
        "/api/adminauth/reset-context/",
        "/api/mobile/bind/",
        "/api/onboarding/",
        "/activate/",
        "/reset-password/",
        "/verify-email/",
        "/onboarding/",
    ];

    /// <summary>A segment after one of the prefixes that is a lookup, not a credential.</summary>
    private static readonly HashSet<string> NotSecrets = new(StringComparer.OrdinalIgnoreCase) { "by-request" };

    private static readonly HashSet<string> SecretQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "code", "key", "secret", "otp", "password", "pwd", "sig", "signature", "access_token", "refresh_token",
    };

    /// <summary>The path (and optional query string) with every credential replaced by <see cref="Redacted"/>.</summary>
    public static string Redact(string? pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery)) return pathAndQuery ?? string.Empty;

        var queryAt = pathAndQuery.IndexOf('?');
        var path = queryAt < 0 ? pathAndQuery : pathAndQuery[..queryAt];
        var query = queryAt < 0 ? null : pathAndQuery[(queryAt + 1)..];

        return RedactPath(path) + (query is null ? string.Empty : "?" + RedactQuery(query));
    }

    /// <summary>The same for a whole URI. Scheme, host and port are kept; user-info (never legitimate here) and fragment are dropped.</summary>
    public static string Redact(Uri? uri)
    {
        if (uri is null) return string.Empty;
        if (!uri.IsAbsoluteUri) return Redact(uri.OriginalString);
        return uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo + "@", string.Empty) + Redact(uri.PathAndQuery);
    }

    private static string RedactPath(string path)
    {
        foreach (var prefix in SecretFollows)
        {
            var at = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;

            var start = at + prefix.Length;
            var end = path.IndexOf('/', start);
            if (end < 0) end = path.Length;
            if (end == start) continue;                                  // nothing follows the prefix
            if (NotSecrets.Contains(path[start..end])) continue;         // a lookup route, not a credential

            return path[..start] + Redacted + RedactPath(path[end..]);
        }

        return path;
    }

    private static string RedactQuery(string query)
    {
        var parts = query.Split('&');
        var sb = new StringBuilder(query.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append('&');
            var eq = parts[i].IndexOf('=');
            if (eq > 0 && SecretQueryKeys.Contains(Uri.UnescapeDataString(parts[i][..eq])))
                sb.Append(parts[i][..(eq + 1)]).Append(Redacted);
            else
                sb.Append(parts[i]);
        }
        return sb.ToString();
    }
}
