namespace EEMOCantilanSDS.Application.Common.Tenancy
{
    /// <summary>
    /// Optional per-request tenant override for the rare flow that must run under a tenant OTHER than the
    /// one the authenticated user (or the default fallback) would resolve to — specifically the anonymous
    /// PayMongo webhook, which carries no JWT but must settle a payment under the transaction's own LGU
    /// (not the default tenant). Null/empty by default, so every ordinary request is completely unaffected.
    /// Scoped per request; both <c>ICurrentMunicipalityAccessor</c> and <c>ITenantContext</c> consult it first.
    /// </summary>
    public interface IRequestTenantScope
    {
        /// <summary>The overriding municipality id, or null when no override is active.</summary>
        System.Guid? MunicipalityId { get; }

        /// <summary>The overriding tenant (cache) code, or null when no override is active.</summary>
        string? TenantCode { get; }

        /// <summary>Pins this request to a specific tenant (ignored when the id is empty).</summary>
        void Use(System.Guid municipalityId, string tenantCode);
    }
}
