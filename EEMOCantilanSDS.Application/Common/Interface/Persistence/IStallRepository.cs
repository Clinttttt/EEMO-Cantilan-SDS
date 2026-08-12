using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IStallRepository
{
    Task<IReadOnlyList<StallDto>> GetStallsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct);
    Task<CursorPagedResult<StallDto>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section, DateTime? cursor, int pageSize, CancellationToken ct);
    Task<StallHoldersListDto> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct);
    /// <remarks>
    /// The collector app's two whole-screen projections live on <see cref="IStallMobileQueries"/>. They are a different
    /// shape from anything the office reads, and a handler serving the app has no business with stall aggregates or
    /// number uniqueness.
    /// </remarks>
    Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    /// <remarks>
    /// The follow-up reads have moved off this contract: contracts needing attention are on
    /// <see cref="IContractAttentionQueries"/> and the register of inactive accounts is on
    /// <see cref="IClosedStallAccountQueries"/>. Both are reports, and a report should not be able to let, transfer,
    /// renew or close the stalls it reports on.
    /// </remarks>
    Task<Stall?> GetByIdAsync(Guid id, CancellationToken ct);
    /// <summary>The facility code that a stall belongs to, or null if the stall is not found. Used to route
    /// online-payment notifications to that facility's assigned collectors.</summary>
    Task<FacilityCode?> GetFacilityCodeByStallIdAsync(Guid stallId, CancellationToken ct);
    Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct);
    /// <summary>
    /// All stalls in a facility (section-scoped for NPM) with their contracts, TRACKED — used by bulk
    /// import to decide per row whether to create a new stall or renew an existing expired/closed one.
    /// </summary>
    Task<IReadOnlyList<Stall>> GetStallsWithContractsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, CancellationToken ct);
    Task AddAsync(Stall stall, CancellationToken ct);
    Task AddContractAsync(Contract contract, CancellationToken ct);
    Task UpdateAsync(Stall stall, CancellationToken ct);
    Task<bool> IsStallNoUniqueAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, string stallNo, CancellationToken ct);

    /// <summary>
    /// The stall already carrying this number in the same facility (and, for NPM, the same section), with its
    /// contracts so the caller can tell whether it is occupied. Null when the number is free.
    /// </summary>
    Task<Stall?> FindStallByNumberAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, string stallNo, CancellationToken ct);
}
