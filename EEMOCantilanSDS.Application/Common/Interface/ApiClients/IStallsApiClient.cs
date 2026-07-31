using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;
using EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IStallsApiClient
{
    Task<Result<StallHoldersListDto>> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section = null, string? searchTerm = null);
    Task<Result<CursorPagedResult<StallDto>>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section = null, DateTime? cursor = null, int pageSize = 20);
    Task<Result<StallDto>> CreateStallAsync(CreateStallCommand command);

    /// <summary>
    /// Prepares placing a payor whose occupancy ended into a stall of their own — same facility and section, the
    /// terms they were on, and a suggested number. Reads only; the old account is not affected.
    /// </summary>
    /// <param name="contractId">
    /// The term being continued. Required in practice on a re-let stall, where the stall's latest term belongs to
    /// the lessee sitting there now.
    /// </param>
    Task<Result<StallReassignmentPreviewDto>> GetStallReassignmentPreviewAsync(Guid previousStallId, Guid? contractId = null);

    /// <summary>
    /// Registers that new stall. Any balance on the payor's old account stays there, where it was incurred.
    /// </summary>
    Task<Result<StallDto>> AssignPastOccupantStallAsync(AssignPastOccupantStallCommand command);
    Task<Result<NpmRatesDto>> GetNpmRatesAsync();
    Task<Result<BulkImportResultDto>> BulkImportStallholdersAsync(BulkImportStallholdersCommand command);
    Task<Result<StallDto>> UpdateStallAsync(Guid stallId, UpdateStallCommand command);
    Task<Result<bool>> ToggleStallStatusAsync(Guid stallId, bool close);
    /// <summary>Removes an inactive (closed/expired) stall account — soft-delete, frees the number, keeps history. SuperAdmin-only.</summary>
    Task<Result<bool>> SoftDeleteStallAsync(Guid stallId);
    Task<Result<bool>> UpdateStallDetailsAsync(Guid stallId, UpdateStallDetailsCommand command);
    Task<Result<IReadOnlyList<ClosedStallAccountDto>>> GetClosedStallAccountsAsync();
    Task<Result<bool>> RenewStallContractAsync(Guid stallId, RenewStallContractRequest request);
    Task<Result<CursorPagedResult<StallCollectionHistoryRowDto>>> GetStallCollectionHistoryAsync(Guid stallId, DateTime? cursor = null, int pageSize = 10);
}
