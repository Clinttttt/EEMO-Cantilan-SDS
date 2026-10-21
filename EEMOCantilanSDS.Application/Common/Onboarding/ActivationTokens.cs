using System.Security.Cryptography;
using System.Text;

namespace EEMOCantilanSDS.Application.Common.Onboarding;

/// <summary>
/// One-time activation tokens for the set-password link: cryptographically random, url-safe, and stored only as a SHA-256 hash.
/// The same primitive the municipal Head's activation uses, so an invited Admin's link is verified by the same single-use,
/// expiring, tenant-bound code path (<c>SetAdminPasswordByTokenCommandHandler</c>) rather than a second password engine.
/// </summary>
public static class ActivationTokens
{
    /// <summary>How long an activation or invitation link stays usable.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static (string Raw, string Hash) Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Hash(raw));
    }

    public static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>A random password nobody is told: the placeholder an invited account holds until its owner sets their own.</summary>
    public static string RandomPlaceholderPassword()
    {
        Span<byte> bytes = stackalloc byte[18];
        RandomNumberGenerator.Fill(bytes);
        return $"Aa1!{Convert.ToBase64String(bytes).Replace('+', 'K').Replace('/', 'z').TrimEnd('=')}";
    }
}
