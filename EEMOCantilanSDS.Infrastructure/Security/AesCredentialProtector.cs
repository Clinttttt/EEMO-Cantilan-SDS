using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Infrastructure.Security;

/// <summary>
/// AES-256-GCM implementation of <see cref="ICredentialProtector"/>. The 256-bit key is read from
/// configuration (<c>Encryption:Key</c>, a 32-byte base64 value) on demand — so environments that never
/// store per-LGU gateway keys (e.g. Cantilan-only) never need the key configured. Output is
/// base64(nonce[12] ‖ tag[16] ‖ ciphertext), which is authenticated (tamper-evident).
/// </summary>
public sealed class AesCredentialProtector(IConfiguration configuration) : ICredentialProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private byte[] ResolveKey()
    {
        var b64 = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException(
                "Encryption:Key is not configured. Set it to a 32-byte (256-bit) base64 value to protect gateway credentials.");

        byte[] key;
        try { key = Convert.FromBase64String(b64.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Encryption:Key must be a valid base64 value."); }

        if (key.Length != 32)
            throw new InvalidOperationException("Encryption:Key must decode to exactly 32 bytes (256-bit).");
        return key;
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var key = ResolveKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var combined = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(combined);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        var key = ResolveKey();
        var combined = Convert.FromBase64String(ciphertext);
        if (combined.Length < NonceSize + TagSize)
            throw new InvalidOperationException("Ciphertext is too short to be valid.");

        var nonce = combined[..NonceSize];
        var tag = combined[NonceSize..(NonceSize + TagSize)];
        var cipher = combined[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
