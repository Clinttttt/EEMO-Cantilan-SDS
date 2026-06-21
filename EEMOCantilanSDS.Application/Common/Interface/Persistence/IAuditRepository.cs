using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Dtos.Audit;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IAuditRepository
{
    /// <summary>
    /// Returns a filtered, offset-paginated page of audit entries plus action-summary counts and
    /// the distinct actor/entity values for the filter dropdowns. All filtering/aggregation runs
    /// server-side. <paramref name="fromUtc"/>/<paramref name="toUtc"/> are inclusive UTC bounds.
    /// </summary>
    Task<AuditTrailDto> GetAuditTrailAsync(
        string? search,
        string? action,
        string? entityType,
        string? actor,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        bool includeOptions,
        CancellationToken ct);
}
