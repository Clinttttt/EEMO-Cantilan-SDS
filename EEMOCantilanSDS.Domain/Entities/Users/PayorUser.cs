using EEMOCantilanSDS.Domain.Constants;
using Microsoft.AspNetCore.Identity;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    /// <summary>
    /// A stall payor (lessee/occupant) who self-activates an online account to view balances and
    /// pay rent online. Reuses the <see cref="BaseUser"/> TPH table (discriminator "Payor"): password
    /// hashing, refresh tokens, and lockout all behave like the other user types. Payors carry NO new
    /// scalar columns — they log in with their registered contact number stored in
    /// <see cref="BaseUser.Username"/> and have no email.
    /// </summary>
    public class PayorUser : BaseUser
    {
        /// <summary>Stalls/contracts this payor is allowed to view and pay for.</summary>
        public ICollection<PayorStallLink> StallLinks { get; private set; } = new List<PayorStallLink>();

        private PayorUser() { }

        /// <summary>
        /// Creates a self-activated payor account. <paramref name="contactNumber"/> doubles as the
        /// login identifier (stored in Username); email is intentionally null.
        /// </summary>
        public static PayorUser Create(string fullName, string contactNumber, string password)
        {
            return new PayorUser
            {
                Id = Guid.NewGuid(),
                FullName = fullName,
                Username = contactNumber,
                Email = null,
                PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, password),
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "Self-Activation"
            };
        }

        public void RecordLogin()
        {
            LastLoginAt = DateTime.UtcNow;
            FailedAttempts = 0;
            LockedUntil = null;
        }

        public void RecordFailedLogin()
        {
            FailedAttempts++;
            if (FailedAttempts >= DomainRules.MaxFailedLoginAttempts)
                LockedUntil = DateTime.UtcNow.AddMinutes(DomainRules.LockoutMinutes);
        }

        public void ResetPassword(string newPassword)
        {
            PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, newPassword);
            FailedAttempts = 0;
            LockedUntil = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "Self-Service";
        }

        public bool IsLockedOut => LockedUntil.HasValue && LockedUntil > DateTime.UtcNow;
    }
}
