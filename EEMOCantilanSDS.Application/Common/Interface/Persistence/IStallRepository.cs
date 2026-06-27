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
    /// Inactive stall accounts for the register: explicitly CLOSED (frozen) stalls and EXPIRED ones
    /// (active stall whose contract term has lapsed). Includes lifetime collected (all money ever
    /// received) and uncollected arrears accrued up to the end point (close date / contract expiry),
    /// excused/absent-aware.
    /// </summary>
    Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsAsync(CancellationToken ct);
    Task<Stall?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct);
    Task AddAsync(Stall stall, CancellationToken ct);
    Task AddContractAsync(Contract contract, CancellationToken ct);
    Task UpdateAsync(Stall stall, CancellationToken ct);
    Task<bool> IsStallNoUniqueAsync(FacilityCode facilityCode, MarketSection? section, string stallNo, CancellationToken ct);
}
