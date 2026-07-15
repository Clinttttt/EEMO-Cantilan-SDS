using EEMOCantilanSDS.Domain.Common;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
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
