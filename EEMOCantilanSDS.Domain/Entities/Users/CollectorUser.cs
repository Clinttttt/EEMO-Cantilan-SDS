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
       string email,
       string contactNumber,
       string password)
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
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }
        public void UpdateProfile(
       string fullName,
       string contactNumber,
       string email,
       string updatedBy)
        {
            FullName = fullName;
            ContactNumber = contactNumber;
            Email = email;
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
