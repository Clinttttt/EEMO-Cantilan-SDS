namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Symmetric encryption for secrets stored at rest (e.g. per-LGU payment-gateway keys). Implementations
/// use an app-managed key; a round-trip of <see cref="Protect"/> then <see cref="Unprotect"/> returns the
/// original value.
/// </summary>
public interface ICredentialProtector
{
    /// <summary>Encrypts a plaintext secret for storage. Empty input returns empty.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a value produced by <see cref="Protect"/>. Empty input returns empty.</summary>
    string Unprotect(string ciphertext);
}
