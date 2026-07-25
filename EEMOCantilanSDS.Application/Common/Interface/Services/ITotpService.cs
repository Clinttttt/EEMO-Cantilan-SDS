using System.Threading;

namespace EEMOCantilanSDS.Application.Common.Interface.Services
{
    /// <summary>
    /// Time-based one-time password engine (RFC 6238), used for authenticator-app two-factor sign-in.
    /// <para>
    /// Implemented over the BCL's HMAC primitives — no third-party OTP dependency — and verified against the
    /// RFC's published test vectors. Codes are 6 digits over 30-second steps, which is what Google
    /// Authenticator, Microsoft Authenticator and every other standard app expects.
    /// </para>
    /// </summary>
    public interface ITotpService
    {
        /// <summary>Generates a new base32 shared secret (160-bit, the RFC-recommended size for HMAC-SHA1).</summary>
        string GenerateSecret();

        /// <summary>
        /// Builds the standard <c>otpauth://totp/...</c> provisioning URI that authenticator apps consume
        /// (usually via QR). <paramref name="issuer"/> and <paramref name="account"/> are what the user sees
        /// in their app, so they should identify the office and the username.
        /// </summary>
        string BuildProvisioningUri(string secretBase32, string issuer, string account);

        /// <summary>
        /// Validates a user-entered code against the secret, accepting a small clock drift.
        /// </summary>
        /// <param name="minimumStep">
        /// The last time-step already consumed by this account, or null. A code from that step or earlier is
        /// REJECTED, which stops an observed code being replayed inside its own validity window.
        /// </param>
        /// <param name="matchedStep">The time-step the code matched, to be persisted as the new minimum.</param>
        bool TryValidate(string secretBase32, string code, long? minimumStep, out long matchedStep);

        /// <summary>The current time-step (Unix seconds / 30). Exposed for tests and drift diagnostics.</summary>
        long CurrentStep();
    }
}
