using EEMOCantilanSDS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    public abstract class BaseUser : AuditableEntity
    {
        public string? FullName { get; protected set; }
        public string? Username { get; protected set; } 
        public string? Email { get; protected set; }
        public string PasswordHash { get; protected set; } = string.Empty;
        public  bool IsActive { get; protected set; }
        public  bool MustChangePassword { get; protected set; }
        public int FailedAttempts { get; protected set; }
        public DateTime? LockedUntil { get; protected set; }
        public  DateTime? LastLoginAt { get; protected set; }

  
        public string? RefreshToken { get; protected set; }
        public DateTime? RefreshTokenExpiryTime { get; protected set; }

        
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
    }
}
