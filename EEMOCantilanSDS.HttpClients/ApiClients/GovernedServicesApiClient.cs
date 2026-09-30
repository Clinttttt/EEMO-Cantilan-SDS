using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class GovernedServicesApiClient(HttpClient http) : HandleResponse(http), IGovernedServicesApiClient
{
    public Task<Result<IReadOnlyList<GovernedServiceDefinitionDto>>> GetDefinitionsAsync() =>
        GetAsync<IReadOnlyList<GovernedServiceDefinitionDto>>("api/governed-services");

    public Task<Result<GovernedServiceDefinitionDto>> ConfigureAsync(string operationCode, ConfigureGovernedServiceRequest request) =>
        PutAsync<ConfigureGovernedServiceRequest, GovernedServiceDefinitionDto>(
            $"api/governed-services/{Uri.EscapeDataString(operationCode)}", request);

    public Task<Result<IReadOnlyList<GovernedServiceActivityDto>>> GetActivityAsync(string operationCode, DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<GovernedServiceActivityDto>>(
            $"api/governed-services/{Uri.EscapeDataString(operationCode)}/activity?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
}
