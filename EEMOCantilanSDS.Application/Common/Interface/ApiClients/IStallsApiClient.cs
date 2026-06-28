using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
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
    Task<Result<StallDto>> UpdateStallAsync(Guid stallId, UpdateStallCommand command);
    Task<Result<bool>> ToggleStallStatusAsync(Guid stallId, bool close);
    Task<Result<bool>> UpdateStallDetailsAsync(Guid stallId, UpdateStallDetailsCommand command);
    Task<Result<IReadOnlyList<ClosedStallAccountDto>>> GetClosedStallAccountsAsync();
    Task<Result<bool>> RenewStallContractAsync(Guid stallId, RenewStallContractRequest request);
    Task<Result<CursorPagedResult<StallCollectionHistoryRowDto>>> GetStallCollectionHistoryAsync(Guid stallId, DateTime? cursor = null, int pageSize = 10);
}
