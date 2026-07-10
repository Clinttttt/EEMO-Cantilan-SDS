using EEMOCantilanSDS.Application.Dtos.SystemHealth;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Reads the CALLER'S tenant storage footprint only. The implementation scopes every figure to the
/// current municipality (never other tenants, never the whole database).
/// </summary>
public interface ITenantUsageRepository
{
    Task<TenantUsageDto> GetUsageAsync(CancellationToken ct);
}
