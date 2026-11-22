using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Tenant-scoped, authenticated ST-20 monthly-rent readiness and governed activation.</summary>
public interface IRentActivationsApiClient
{
    Task<Result<RentActivationPlanDto>> PlanAsync(RentActivationPlanRequest request);
    Task<Result<RentActivationResultDto>> ActivateAsync(RentActivationRequest request);
}
