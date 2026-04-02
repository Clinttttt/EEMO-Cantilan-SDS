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
            AdminRole role)
        {
            return new AdminUser
            {
                Id = Guid.NewGuid(),
                FullName = fullName,
                Username = username,
                Email = email,
                PasswordHash = new PasswordHasher<BaseUser>()
                                   .HashPassword(null!, password),
                Role = role,
                IsActive = true,
                MustChangePassword = true,  
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }
        public void UpdateProfile(string fullName, string email, string updatedBy)
        {
            FullName = fullName;
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

        public void RecordLogin()
        {
            FailedAttempts = 0;
            LockedUntil = null;
            MustChangePassword = false;
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
