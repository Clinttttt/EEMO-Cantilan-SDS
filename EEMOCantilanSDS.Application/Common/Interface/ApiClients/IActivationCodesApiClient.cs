using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Head Portal client for staff issuance of historical-account payor codes (ST-19). The server owns eligibility.</summary>
public interface IActivationCodesApiClient
{
    Task<Result<HistoricalAccessCodeDto>> IssueHistoricalAsync(IssueHistoricalAccessRequest request);
}
