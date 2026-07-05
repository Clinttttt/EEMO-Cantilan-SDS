using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    public class AdminUser : BaseUser
    {
        public AdminRole Role { get; private set; }
        private AdminUser() { }
        public static AdminUser Create(string fullName,string
            username,string 
            email,string password,
            AdminRole role,
            Guid municipalityId = default,
            bool isActive = true)
        {
            return new AdminUser
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FullName = fullName,
                Username = username,
                Email = email,
                PasswordHash = new PasswordHasher<BaseUser>()
                                   .HashPassword(null!, password),
                Role = role,
                IsActive = isActive,
                MustChangePassword = true,  
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }
        public void UpdateProfile(string fullName, string username, string email, string updatedBy)
        {
            FullName = fullName;
            Username = username;
            Email = email;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void ChangeRole(AdminRole newRole, string updatedBy)
        {
            Role = newRole;
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
            FailedAttempts = 0;
            LockedUntil = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        // Head-initiated password reset. Forces a change on next login and clears any lockout
        // so a forgotten-password account can immediately sign in with the temporary password.
        public void ResetPassword(string newPassword, string updatedBy)
        {
            PasswordHash = new PasswordHasher<BaseUser>().HashPassword(null!, newPassword);
            MustChangePassword = true;
            FailedAttempts = 0;
            LockedUntil = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void RecordLogin()
        {
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

    public enum AdminRole
    {
        SuperAdmin = 1,
        Admin = 2,
    }



}
