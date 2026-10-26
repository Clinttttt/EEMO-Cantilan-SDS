using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class ActivationCodesApiClient(HttpClient http) : HandleResponse(http), IActivationCodesApiClient
{
    public Task<Result<HistoricalAccessCodeDto>> IssueHistoricalAsync(IssueHistoricalAccessRequest request) =>
        PostAsync<IssueHistoricalAccessRequest, HistoricalAccessCodeDto>("api/activation-codes/historical", request);
}
