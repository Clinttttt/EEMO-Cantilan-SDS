using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IStallsApiClient
{
    Task<Result<StallHoldersListDto>> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section = null, string? searchTerm = null);
    Task<Result<CursorPagedResult<StallDto>>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section = null, DateTime? cursor = null, int pageSize = 20);
    Task<Result<StallDto>> CreateStallAsync(CreateStallCommand command);
    Task<Result<StallDto>> UpdateStallAsync(Guid stallId, UpdateStallCommand command);
}
