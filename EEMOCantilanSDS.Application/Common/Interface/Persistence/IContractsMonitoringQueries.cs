using EEMOCantilanSDS.Application.Dtos.Monitoring;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>Bounded, current-tenant, read-only contract register. No assessment or lifecycle writes.</summary>
public interface IContractsMonitoringQueries
{
    Task<ContractsMonitoringPageDto> GetAsync(ContractsMonitoringRequest filters, DateOnly today, CancellationToken ct);
}
