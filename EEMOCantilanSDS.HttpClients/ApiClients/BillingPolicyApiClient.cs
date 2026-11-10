using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class BillingPolicyApiClient(HttpClient http) : HandleResponse(http), IBillingPolicyApiClient
{
    public Task<Result<BillingPolicyPageDto>> GetAsync() => GetAsync<BillingPolicyPageDto>("api/billing-policy");
    public Task<Result<BillingPolicyPageDto>> RecordAsync(RecordBillingPolicyRequest request) =>
        PostAsync<RecordBillingPolicyRequest, BillingPolicyPageDto>("api/billing-policy", request);
}
