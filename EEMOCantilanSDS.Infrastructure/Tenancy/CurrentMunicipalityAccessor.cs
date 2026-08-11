using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Infrastructure.Tenancy
{
    /// <summary>
    /// Per-request resolver for the current municipality id. Registered as scoped so each request resolves
    /// its own tenant from the authenticated user. Resolution order:
    /// <list type="number">
    ///   <item>an explicit per-request override (the anonymous PayMongo webhook pins its transaction's LGU), else</item>
    ///   <item>for an AUTHENTICATED caller, their own municipality — and nothing else, else</item>
    ///   <item>for a token-less caller, the default municipality populated at startup.</item>
    /// </list>
    ///
    /// <para>
    /// An authenticated caller with no municipality of their own resolves to <see cref="System.Guid.Empty"/> — unresolved
    /// — and the query filter shows them nothing. It used to fall through to the DEFAULT municipality, which meant a
    /// user of any LGU whose claim was missing or malformed read CANTILAN's data and was told it was their own. That is
    /// the fallback this removes. It stays for token-less flows, which have no user to resolve and are the paths that
    /// legitimately need a default: login (which bypasses the filter anyway), activation, webhooks, startup.
    /// </para>
    /// </summary>
    public sealed class CurrentMunicipalityAccessor(ICurrentUserService currentUser, DefaultMunicipalityStore store, IRequestTenantScope scope)
        : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId
        {
            get
            {
                if (scope.MunicipalityId is { } pinned) return pinned;

                // Authenticated: their municipality or nothing. Never another tenant's, and never the default's.
                if (currentUser.IsAuthenticated) return currentUser.MunicipalityId ?? Guid.Empty;

                return store.Default;
            }
        }

        /// <summary>
        /// Sets the default municipality id (ignored when empty). Delegates to the singleton store so the
        /// startup <c>Set</c> still populates the process-wide default that token-less requests fall back to.
        /// </summary>
        public void Set(Guid municipalityId) => store.Set(municipalityId);
    }
}
