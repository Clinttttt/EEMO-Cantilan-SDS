using EEMOCantilanSDS.Application.Dtos.Backup;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Assembles a per-tenant data export containing ONLY the caller's municipality rows (operational and
/// financial data + audit trail; credential tables excluded). Scoped server-side to the current tenant.
/// </summary>
public interface ITenantExportRepository
{
    Task<TenantExportPayload> ExportAsync(CancellationToken ct);
}
