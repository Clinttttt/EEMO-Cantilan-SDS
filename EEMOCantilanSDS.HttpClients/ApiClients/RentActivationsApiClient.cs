using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>Uses the same bearer/refresh pipeline as all other Head Portal management APIs.</summary>
public sealed class RentActivationsApiClient(HttpClient http) : HandleResponse(http), IRentActivationsApiClient
{
    public Task<Result<RentActivationPlanDto>> PlanAsync(RentActivationPlanRequest request) =>
        PostAsync<RentActivationPlanRequest, RentActivationPlanDto>("api/rent-activations/plan", request);

    public Task<Result<RentActivationResultDto>> ActivateAsync(RentActivationRequest request) =>
        PostAsync<RentActivationRequest, RentActivationResultDto>("api/rent-activations", request);
}
