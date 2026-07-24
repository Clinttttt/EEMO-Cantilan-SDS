using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    public class CollectorUser : BaseUser
    {
        public string? EmployeeId { get; private set; }
        public string? ContactNumber { get; private set; }
        public DateTime? LastActiveAt { get; private set; }

        public ICollection<CollectorFacilityAssignment> FacilityAssignments { get; private set; }
     = new List<CollectorFacilityAssignment>();
        private CollectorUser() { }
        public static CollectorUser Create(
       string fullName,
       string employeeId,
       string username,
       string? email,
       string? contactNumber,
       string password,
       Guid municipalityId = default)
        {
            return new CollectorUser
            {
                Id = Guid.NewGuid(),
                FullName = fullName,
                EmployeeId = employeeId,
                Username = username,
                Email = email,
                ContactNumber = contactNumber,
                PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, password),
                IsActive = true,
                MustChangePassword = false,   // Collectors don't need forced password change
                MunicipalityId = municipalityId,   // default (Guid.Empty) is stamped by the interceptor on save
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }
        public void UpdateProfile(
       string fullName,
       string? contactNumber,
       string? email,
       string updatedBy)
        {
            FullName = fullName;
            ContactNumber = contactNumber;
            Email = email;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        // Head-initiated username change (e.g. to a memorable login the collector can actually type).
        // Uniqueness within the LGU is enforced by the caller; this only applies the trimmed value.
        public void ChangeUsername(string username, string updatedBy)
        {
            Username = username.Trim();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void Deactivate(string updatedBy)
        {
            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void Activate(string updatedBy)
        {
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        // Head-initiated password reset for collectors who forgot their mobile-app credentials.
        // Clears any lockout so the collector can sign in again with the temporary password.
        public void ResetPassword(string newPassword, string updatedBy)
        {
            PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, newPassword);
            FailedAttempts = 0;
            LockedUntil = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void RecordLogin()
        {
            LastActiveAt = DateTime.UtcNow;
            FailedAttempts = 0;
            LockedUntil = null;
        }

        public void RecordFailedLogin()
        {
            FailedAttempts++;
            if (FailedAttempts >= Constants.DomainRules.MaxFailedLoginAttempts)
                LockedUntil = DateTime.UtcNow.AddMinutes(Constants.DomainRules.LockoutMinutes);
        }

        public bool IsLockedOut => LockedUntil.HasValue && LockedUntil > DateTime.UtcNow;
    }
}
