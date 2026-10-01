using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view of governed configurable services (IA-044): setup, and the posted-collection activity.</summary>
public interface IGovernedServicesApiClient
{
    Task<Result<IReadOnlyList<GovernedServiceDefinitionDto>>> GetDefinitionsAsync();
    Task<Result<GovernedServiceDefinitionDto>> ConfigureAsync(string operationCode, ConfigureGovernedServiceRequest request);
    Task<Result<IReadOnlyList<GovernedServiceActivityDto>>> GetActivityAsync(string operationCode, DateOnly from, DateOnly to);
}
