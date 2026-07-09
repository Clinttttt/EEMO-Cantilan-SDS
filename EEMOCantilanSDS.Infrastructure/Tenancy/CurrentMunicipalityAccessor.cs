using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Infrastructure.Tenancy
{
    /// <summary>
    /// Per-request resolver for the current municipality id. Registered as scoped so each request resolves
    /// its own tenant from the authenticated user (Phase 5). Resolution order:
    /// <list type="number">
    ///   <item>the authenticated user's municipality (from the JWT <c>municipality_id</c> claim), else</item>
    ///   <item>the default municipality (Cantilan), populated at startup into the singleton store, else</item>
    ///   <item><see cref="System.Guid.Empty"/> (unresolved) — the tenant filter is a no-op, nothing is hidden.</item>
    /// </list>
    /// This preserves Cantilan's single-tenant behaviour byte-for-byte: token-less flows and requests whose
    /// user carries no municipality id fall straight through to the default.
    /// </summary>
    public sealed class CurrentMunicipalityAccessor(ICurrentUserService currentUser, DefaultMunicipalityStore store, IRequestTenantScope scope)
        : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => scope.MunicipalityId ?? currentUser.MunicipalityId ?? store.Default;

        /// <summary>
        /// Sets the default municipality id (ignored when empty). Delegates to the singleton store so the
        /// startup <c>Set</c> still populates the process-wide default that token-less requests fall back to.
        /// </summary>
        public void Set(Guid municipalityId) => store.Set(municipalityId);
    }
}
