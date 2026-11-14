using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IBillingPolicyApiClient
{
    Task<Result<BillingPolicyPageDto>> GetAsync();
    Task<Result<BillingPolicyPageDto>> RecordAsync(RecordBillingPolicyRequest request);
}
