using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Authorization
{
    /// <summary>
    /// Peer-Head protection for account management.
    /// <para>
    /// Account management is already restricted to the Head (SuperAdmin), but a municipality can have more
    /// than one Head. One Head must not be able to edit, reset, disable or otherwise act on ANOTHER Head's
    /// account — that would let peers lock each other out or seize each other's access. A Head may always
    /// act on their OWN account, and on ordinary Admin accounts (which is the point of the role).
    /// </para>
    /// <para>
    /// Enforced server-side in every admin-mutating handler; the UI merely mirrors it by disabling the
    /// buttons, so hiding them is never the only protection.
    /// </para>
    /// </summary>
    public static class AdminManagementGuard
    {
        public const string PeerHeadDenied =
            "Another Head's account can only be managed by that Head. You can manage your own account and Admin accounts.";

        /// <summary>
        /// True when the acting user may mutate <paramref name="target"/>. Only peer-Head access is denied:
        /// any Admin target is allowed, and acting on yourself is always allowed.
        /// </summary>
        public static bool CanActOn(BaseUser target, Guid? actingUserId)
        {
            if (target is not AdminUser admin)
                return true;                                   // collectors are governed elsewhere

            if (admin.Role != AdminRole.SuperAdmin)
                return true;                                   // ordinary Admin accounts are manageable

            // Target is a Head: allowed only when it is the acting user's own account. A missing acting id
            // (background job / token-less call) is treated as NOT the owner, i.e. denied — fail closed.
            return actingUserId is { } id && id == admin.Id;
        }
    }
}
