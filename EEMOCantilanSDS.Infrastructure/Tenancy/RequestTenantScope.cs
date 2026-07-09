using System;
using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Infrastructure.Tenancy
{
    /// <summary>
    /// Scoped (per-request) holder for an explicit tenant override. Empty until a handler calls
    /// <see cref="Use"/> (only the anonymous webhook does today), so ordinary requests resolve their tenant
    /// exactly as before. See <see cref="IRequestTenantScope"/>.
    /// </summary>
    public sealed class RequestTenantScope : IRequestTenantScope
    {
        public Guid? MunicipalityId { get; private set; }
        public string? TenantCode { get; private set; }

        public void Use(Guid municipalityId, string tenantCode)
        {
            if (municipalityId == Guid.Empty) return;
            MunicipalityId = municipalityId;
            if (!string.IsNullOrWhiteSpace(tenantCode))
                TenantCode = tenantCode.Trim();
        }
    }
}
