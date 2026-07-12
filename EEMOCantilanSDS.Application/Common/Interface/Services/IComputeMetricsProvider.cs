using EEMOCantilanSDS.Application.Dtos.SystemHealth;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>Supplies host compute metrics (CPU %, memory %, provisioned storage) for the database server.</summary>
public interface IComputeMetricsProvider
{
    Task<ComputeMetrics> GetAsync(CancellationToken ct);
}
