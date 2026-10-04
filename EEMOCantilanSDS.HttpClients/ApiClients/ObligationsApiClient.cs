using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class ObligationsApiClient(HttpClient http) : HandleResponse(http), IObligationsApiClient
{
    public Task<Result<ObligationWorkspaceDto>> GetWorkspaceAsync(ObligationKind kind) =>
        GetAsync<ObligationWorkspaceDto>($"api/obligations/workspace?kind={(int)kind}");
    public Task<Result<ObligationWorkspaceDto>> GetStatusReportAsync(ObligationKind kind, int year) =>
        GetAsync<ObligationWorkspaceDto>($"api/obligations/status-report?kind={(int)kind}&year={year}");
    public Task<Result<ImportSpaceHoldersResult>> ImportSpaceHoldersAsync(ImportSpaceHoldersRequest request) =>
        PostAsync<ImportSpaceHoldersRequest, ImportSpaceHoldersResult>("api/obligations/import", request);
    public Task<Result<IReadOnlyList<ObligationAccountDto>>> GetAccountsAsync(ObligationKind kind) =>
        GetAsync<IReadOnlyList<ObligationAccountDto>>($"api/obligations/accounts?kind={(int)kind}");

    public Task<Result<IReadOnlyList<ObligationQuoteDto>>> GetRegisterAsync(Guid accountId) =>
        GetAsync<IReadOnlyList<ObligationQuoteDto>>($"api/obligations/accounts/{accountId}/register");

    public Task<Result<IReadOnlyList<VendorFeeStallDto>>> GetVendorStallsAsync() =>
        GetAsync<IReadOnlyList<VendorFeeStallDto>>("api/obligations/vendor-stalls");

    public Task<Result<ObligationAccountDto>> CreateAccountAsync(CreateObligationAccountRequest request) =>
        PostAsync<CreateObligationAccountRequest, ObligationAccountDto>("api/obligations/accounts", request);

    public Task<Result<bool>> SetRateAsync(Guid accountId, SetObligationRateRequest request) =>
        PostAsync<SetObligationRateRequest, bool>($"api/obligations/accounts/{accountId}/rates", request);

    public Task<Result<bool>> CloseAccountAsync(Guid accountId, CloseObligationAccountRequest request) =>
        PostAsync<CloseObligationAccountRequest, bool>($"api/obligations/accounts/{accountId}/close", request);
}
