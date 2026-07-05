using EEMOCantilanSDS.Application.Dtos.SystemHealth;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Read-only access to live PostgreSQL health metrics sourced from pg_* system views.
/// Implementations must be defensive: a single unavailable metric must never fail the whole snapshot.
/// </summary>
public interface IDatabaseHealthRepository
{
    Task<DatabaseHealthDto> GetHealthAsync(CancellationToken ct);
}
