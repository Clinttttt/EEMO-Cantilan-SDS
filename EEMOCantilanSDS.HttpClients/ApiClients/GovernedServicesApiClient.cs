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

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> GetFeeOptionsAsync(string operationCode) =>
        GetAsync<IReadOnlyList<GovernedServiceFeeOptionDto>>($"api/governed-services/{Uri.EscapeDataString(operationCode)}/fee-options");

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> GetFeeOptionsAsync(string operationCode, bool activeOnly) =>
        GetAsync<IReadOnlyList<GovernedServiceFeeOptionDto>>($"api/governed-services/{Uri.EscapeDataString(operationCode)}/fee-options?activeOnly={activeOnly.ToString().ToLowerInvariant()}");

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> AddFeeOptionAsync(string operationCode, AddFeeOptionRequest request) =>
        PostAsync<AddFeeOptionRequest, IReadOnlyList<GovernedServiceFeeOptionDto>>(
            $"api/governed-services/{Uri.EscapeDataString(operationCode)}/fee-options", request);

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> ScheduleFeeOptionRateAsync(
        string operationCode, Guid feeOptionId, ScheduleFeeOptionRateRequest request) =>
        PostAsync<ScheduleFeeOptionRateRequest, IReadOnlyList<GovernedServiceFeeOptionDto>>(
            $"api/governed-services/{Uri.EscapeDataString(operationCode)}/fee-options/{feeOptionId}/rates", request);

    public Task<Result<IReadOnlyList<GovernedServiceFeeOptionDto>>> RetireFeeOptionAsync(
        string operationCode, Guid feeOptionId, RetireFeeOptionRequest request) =>
        PostAsync<RetireFeeOptionRequest, IReadOnlyList<GovernedServiceFeeOptionDto>>(
            $"api/governed-services/{Uri.EscapeDataString(operationCode)}/fee-options/{feeOptionId}/retire", request);

    public Task<Result<IReadOnlyList<FeeOptionTotalDto>>> GetFeeOptionTotalsAsync(string operationCode, DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<FeeOptionTotalDto>>(
            $"api/governed-services/{Uri.EscapeDataString(operationCode)}/fee-option-totals?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
}
