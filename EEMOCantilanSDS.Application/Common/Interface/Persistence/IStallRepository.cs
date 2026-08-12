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
    /// <remarks>
    /// This is the stall AGGREGATE: load a space with its contracts, let it, transfer it, close it, and rule on stall-number
    /// uniqueness. The projections the office and the app merely READ have their own contracts —
    /// <see cref="IStallRegisterQueries"/> for the register, stallholders list and section summaries;
    /// <see cref="IStallMobileQueries"/> for the collector app's two whole-screen projections;
    /// <see cref="IContractAttentionQueries"/> and <see cref="IClosedStallAccountQueries"/> for the follow-up reads. A
    /// handler that only displays spaces has no business with any of the above.
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
