using EEMOCantilanSDS.Domain.Common;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    public abstract class BaseUser : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; protected set; }

        public string? FullName { get; protected set; }
        public string? Username { get; protected set; } 
        public string? Email { get; protected set; }
        public string PasswordHash { get; protected set; } = string.Empty;
        public  bool IsActive { get; protected set; }
        public bool MustChangePassword { get; protected set; }
        public int FailedAttempts { get; protected set; }
        public DateTime? LockedUntil { get; protected set; }
        public  DateTime? LastLoginAt { get; protected set; }

  
        public string? RefreshToken { get; protected set; }
        public DateTime? RefreshTokenExpiryTime { get; protected set; }

        // One-time account-activation token (hashed at rest). Set when an account is provisioned in an
        // inactive, must-set-password state (e.g. an LGU Head at municipality activation); cleared once the
        // user sets their own password through the secure link.
        public string? ActivationTokenHash { get; protected set; }
        public DateTime? ActivationTokenExpiry { get; protected set; }

        // One-time SELF-SERVICE password-reset token (hashed at rest). Deliberately separate from the
        // activation token: an activation token activates the account, whereas a reset token may only
        // change the password — so a reset link can never re-enable a deactivated account.
        public string? PasswordResetTokenHash { get; protected set; }
        public DateTime? PasswordResetTokenExpiry { get; protected set; }

        // Timestamp of the last reset REQUEST (not the reset itself). Used to throttle per-account
        // request bursts so a known address cannot be email-bombed through the anonymous endpoint.
        public DateTime? PasswordResetRequestedAt { get; protected set; }

        /// <summary>
        /// True once the account's email address has been proven to be reachable and owned by the user —
        /// set when they complete activation through the emailed one-time link, or when they confirm an
        /// emailed verification link. Only a verified address is eligible for self-service password reset,
        /// so an unconfirmed (possibly mistyped) address can never be used to take over an account.
        /// </summary>
        public bool EmailVerified { get; protected set; }

        // One-time EMAIL-VERIFICATION token (hashed at rest). Issued when an account is created with an
        // address, or whenever that address changes, so the address can be proven without granting any
        // other capability: confirming it ONLY sets EmailVerified.
        public string? EmailVerificationTokenHash { get; protected set; }
        public DateTime? EmailVerificationTokenExpiry { get; protected set; }

        public void SetRefreshToken(string token, DateTime expiry)
        {
            RefreshToken = token;
            RefreshTokenExpiryTime = expiry;
        }
        public bool IsRefreshTokenValid(string token)
        {
            return RefreshToken == token && RefreshTokenExpiryTime > DateTime.UtcNow;
        }
        public void ClearRefreshToken()
        {
            RefreshToken = null;
            RefreshTokenExpiryTime = null;
        }

        /// <summary>Stamps a one-time activation token (store the HASH, never the raw token).</summary>
        public void SetActivationToken(string tokenHash, DateTime expiry)
        {
            ActivationTokenHash = tokenHash;
            ActivationTokenExpiry = expiry;
        }

        /// <summary>True when the supplied token hash matches an unexpired activation token.</summary>
        public bool IsActivationTokenValid(string tokenHash)
            => !string.IsNullOrEmpty(ActivationTokenHash)
               && ActivationTokenHash == tokenHash
               && ActivationTokenExpiry.HasValue
               && ActivationTokenExpiry.Value > DateTime.UtcNow;

        /// <summary>
        /// Stamps a one-time password-reset token (store the HASH, never the raw token) and records the
        /// request time for throttling. Issuing a new token invalidates any previous one.
        /// </summary>
        public void SetPasswordResetToken(string tokenHash, DateTime expiry, DateTime requestedAt)
        {
            PasswordResetTokenHash = tokenHash;
            PasswordResetTokenExpiry = expiry;
            PasswordResetRequestedAt = requestedAt;
        }

        /// <summary>
        /// True when the supplied token hash matches an unexpired password-reset token. The comparison is
        /// fixed-time so a token cannot be recovered by timing the response.
        /// </summary>
        public bool IsPasswordResetTokenValid(string tokenHash)
        {
            if (string.IsNullOrEmpty(PasswordResetTokenHash) || string.IsNullOrEmpty(tokenHash))
                return false;
            if (!PasswordResetTokenExpiry.HasValue || PasswordResetTokenExpiry.Value <= DateTime.UtcNow)
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(PasswordResetTokenHash),
                Encoding.UTF8.GetBytes(tokenHash));
        }

        /// <summary>Clears any outstanding password-reset token (single use / invalidation).</summary>
        public void ClearPasswordResetToken()
        {
            PasswordResetTokenHash = null;
            PasswordResetTokenExpiry = null;
        }

        /// <summary>
        /// Completes a self-service password reset: sets the new password, consumes the one-time token, and
        /// clears any lockout so the user can sign in immediately. Deliberately does NOT change IsActive —
        /// a reset link can never re-enable a deactivated account — and does NOT set MustChangePassword,
        /// because the user just chose this password themselves.
        /// </summary>
        public void CompletePasswordReset(string newPassword)
        {
            PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, newPassword);
            MustChangePassword = false;
            FailedAttempts = 0;
            LockedUntil = null;
            ClearPasswordResetToken();
            ClearRefreshToken();          // sign out every existing session after a credential change
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>Marks the account's email address as verified (proven reachable and owned).</summary>
        public void MarkEmailVerified()
        {
            if (EmailVerified) return;
            EmailVerified = true;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Stamps a one-time email-verification token (store the HASH, never the raw token). Issuing a new
        /// token invalidates any previous one.
        /// </summary>
        public void SetEmailVerificationToken(string tokenHash, DateTime expiry)
        {
            EmailVerificationTokenHash = tokenHash;
            EmailVerificationTokenExpiry = expiry;
        }

        /// <summary>
        /// True when the supplied token hash matches an unexpired email-verification token. Fixed-time
        /// comparison so the token cannot be recovered by timing the response.
        /// </summary>
        public bool IsEmailVerificationTokenValid(string tokenHash)
        {
            if (string.IsNullOrEmpty(EmailVerificationTokenHash) || string.IsNullOrEmpty(tokenHash))
                return false;
            if (!EmailVerificationTokenExpiry.HasValue || EmailVerificationTokenExpiry.Value <= DateTime.UtcNow)
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(EmailVerificationTokenHash),
                Encoding.UTF8.GetBytes(tokenHash));
        }

        /// <summary>
        /// Confirms the email address via its one-time link: marks it verified. Grants nothing else — it
        /// never activates an account or changes a password.
        /// <para>
        /// The token is deliberately NOT consumed here: it stays usable until it expires (or until the
        /// address changes, which clears it). Because confirming only sets a flag, replaying the link is
        /// harmless — and keeping it idempotent means a page refresh, a forwarded copy, or a prerender +
        /// interactive double-render cannot leave the user staring at "link already used".
        /// </para>
        /// </summary>
        public void ConfirmEmail()
        {
            if (EmailVerified) return;      // already confirmed — nothing to change
            EmailVerified = true;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Called when the account's email address is replaced: the new address has NOT been proven, so the
        /// verified flag is cleared (otherwise a changed address would inherit the old one's trust and could
        /// be used to receive password-reset links). Any outstanding verification token is invalidated too.
        /// </summary>
        protected void OnEmailChanged()
        {
            EmailVerified = false;
            EmailVerificationTokenHash = null;
            EmailVerificationTokenExpiry = null;
        }

        /// <summary>Sets the account's sign-in username (chosen by the user at activation). Caller
        /// guarantees it is normalized (trimmed/lower-cased) and unique within the municipality.</summary>
        public void SetUsername(string username)
        {
            Username = username;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Completes activation: sets the user's chosen password, activates the account, and clears the
        /// one-time token and the must-change flag (they just chose their own password). Also clears any
        /// lockout so they can sign in immediately.
        /// </summary>
        public void CompleteActivation(string newPassword)
        {
            PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, newPassword);
            IsActive = true;
            MustChangePassword = false;
            FailedAttempts = 0;
            LockedUntil = null;
            ActivationTokenHash = null;
            ActivationTokenExpiry = null;
            // Completing activation proves the emailed link reached this address, so it is now verified —
            // which is what makes the account eligible for self-service password reset later.
            EmailVerified = true;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Verifies a plaintext password against this user's stored hash. Used for sensitive-action
        /// re-authentication (e.g. the Head confirming their identity before resetting a password).
        /// </summary>
        public bool VerifyPassword(string password)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(PasswordHash))
                return false;

            return new PasswordHasher<BaseUser>()
                .VerifyHashedPassword(this, PasswordHash, password) != PasswordVerificationResult.Failed;
        }
    }
}
