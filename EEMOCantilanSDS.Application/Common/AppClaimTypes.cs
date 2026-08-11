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
        public const string MunicipalityId = "municipality_id";

    /// <summary>
    /// Marks a DEDICATED platform/console operator — an account belonging to no LGU and holding no municipal office.
    ///
    /// <para>Carried on the token so the API's authorization policy can decide without a database round trip, and so it
    /// decides by the same fact the Application guard reads from the database. Absent or anything other than "true"
    /// means not an operator; a token issued before this claim existed simply falls back to the default-tenant
    /// SuperAdmin clause, so nobody is locked out by the change.</para>
    /// </summary>
    public const string PlatformOperator = "platform_operator";
    }
}
