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
    Task<MobileNpmCollectionDto> GetMobileNpmCollectionAsync(int year, int month, DateOnly collectionDate, CancellationToken ct);
    Task<MobileMonthlyCollectionDto> GetMobileMonthlyCollectionAsync(FacilityCode facilityCode, int year, int month, DateOnly collectionDate, CancellationToken ct);
    Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    /// <summary>
    /// Occupied stalls whose active contract is expired or expiring within <paramref name="withinMonths"/>
    /// — the contract-attention source for the Follow-up Queue. Expired rows are returned first.
    /// </summary>
    Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsync(int withinMonths, CancellationToken ct);
    /// <summary>
    /// Period-scoped contract attention for the Follow-up History (past-period snapshot). Evaluates
    /// expiry/expiring-soon as of the LAST day of <paramref name="year"/>/<paramref name="month"/> instead
    /// of "today", so a past month reflects the contract state that would have shown then.
    /// </summary>
    Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsOfAsync(int year, int month, int withinMonths, CancellationToken ct);
    /// <summary>
    /// Inactive stall accounts for the register: explicitly CLOSED (frozen) stalls and EXPIRED ones
    /// (active stall whose contract term has lapsed). Includes lifetime collected (all money ever
    /// received) and uncollected arrears accrued up to the end point (close date / contract expiry),
    /// excused/absent-aware.
    /// </summary>
    Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsAsync(CancellationToken ct);
    Task<Stall?> GetByIdAsync(Guid id, CancellationToken ct);
    /// <summary>The facility code that a stall belongs to, or null if the stall is not found. Used to route
    /// online-payment notifications to that facility's assigned collectors.</summary>
    Task<FacilityCode?> GetFacilityCodeByStallIdAsync(Guid stallId, CancellationToken ct);
    Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct);
    /// <summary>
    /// All stalls in a facility (section-scoped for NPM) with their contracts, TRACKED — used by bulk
    /// import to decide per row whether to create a new stall or renew an existing expired/closed one.
    /// </summary>
    Task<IReadOnlyList<Stall>> GetStallsWithContractsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct);
    Task AddAsync(Stall stall, CancellationToken ct);
    Task AddContractAsync(Contract contract, CancellationToken ct);
    Task UpdateAsync(Stall stall, CancellationToken ct);
    Task<bool> IsStallNoUniqueAsync(FacilityCode facilityCode, MarketSection? section, string stallNo, CancellationToken ct);
}
