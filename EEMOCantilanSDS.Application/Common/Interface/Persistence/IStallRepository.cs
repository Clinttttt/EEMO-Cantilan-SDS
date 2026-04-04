using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IStallRepository
{
    Task<IReadOnlyList<StallDto>> GetStallsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct);
    Task<CursorPagedResult<StallDto>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section, DateTime? cursor, int pageSize, CancellationToken ct);
    Task<StallHoldersListDto> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct);
    Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    Task<Stall?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(Stall stall, CancellationToken ct);
}
