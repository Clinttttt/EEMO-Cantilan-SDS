using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view and Head setup of the specialized obligation accounts (Fish/Meat Vendor Fee, Kanmanggay, event lot rental).</summary>
public interface IObligationsApiClient
{
    Task<Result<IReadOnlyList<SpaceRentalOperationDto>>> GetSpaceOperationsAsync() =>
        Task.FromResult(Result<IReadOnlyList<SpaceRentalOperationDto>>.Failure("Space operations unavailable."));
    Task<Result<SpaceHolderImportPreview>> PreviewSpaceHoldersAsync(ImportSpaceHoldersRequest request) =>
        Task.FromResult(Result<SpaceHolderImportPreview>.Failure("Space preview unavailable."));
    Task<Result<ObligationWorkspaceDto>> GetWorkspaceAsync(ObligationKind kind) =>
        Task.FromResult(Result<ObligationWorkspaceDto>.Failure("Space workspace unavailable."));
    Task<Result<ObligationWorkspaceDto>> GetStatusReportAsync(ObligationKind kind, int year) =>
        Task.FromResult(Result<ObligationWorkspaceDto>.Failure("Status report unavailable."));
    Task<Result<ImportSpaceHoldersResult>> ImportSpaceHoldersAsync(ImportSpaceHoldersRequest request) =>
        Task.FromResult(Result<ImportSpaceHoldersResult>.Failure("Space import unavailable."));
    Task<Result<IReadOnlyList<ObligationAccountDto>>> GetAccountsAsync(ObligationKind kind);
    Task<Result<IReadOnlyList<ObligationQuoteDto>>> GetRegisterAsync(Guid accountId);
    Task<Result<IReadOnlyList<VendorFeeStallDto>>> GetVendorStallsAsync();
    Task<Result<ObligationAccountDto>> CreateAccountAsync(CreateObligationAccountRequest request);
    Task<Result<bool>> SetRateAsync(Guid accountId, SetObligationRateRequest request);
    Task<Result<bool>> CloseAccountAsync(Guid accountId, CloseObligationAccountRequest request);
}
