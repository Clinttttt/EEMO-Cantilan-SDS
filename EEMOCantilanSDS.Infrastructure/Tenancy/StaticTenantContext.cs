using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Infrastructure.Tenancy;

public sealed class StaticTenantContext : ITenantContext
{
    public string TenantCode => TenantConstants.DefaultTenantCode;
}
