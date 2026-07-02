using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Common
{

    public static class AppClaimTypes
    {
        public const string UserId = ClaimTypes.NameIdentifier;
        public const string FullName = ClaimTypes.Name;
        public const string Email = ClaimTypes.Email;
        public const string Role = ClaimTypes.Role;
        public const string Username = "username";
        public const string IsActive = "is_active";
        public const string MustChangePassword = "must_change_password";
        public const string Municipality = "municipality";
    }
}
