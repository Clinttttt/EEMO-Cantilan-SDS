using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// The stall register as the office reads it: the spaces in a facility, who holds them, and how each section stands for a
/// month.
///
/// <para>
/// These are the reads behind the facility pages, the stallholders list and the utility register. None of them lets,
/// transfers or closes a space, and none of them needs stall-number uniqueness — the rules that make
/// <see cref="IStallRepository"/> an aggregate repository.
/// </para>
///
/// <para>
/// Every row here is identified by <see cref="StallDto"/>'s own id, never by facility + number: the market numbers spaces
/// per section, so NPM genuinely has three stalls called "1". A space-only vendor with no contract has no stall number at
/// all, and the register must still be able to name them.
/// </para>
/// </summary>
public interface IStallRegisterQueries
{
    /// <summary>The spaces in a facility, optionally narrowed to one section.</summary>
    Task<IReadOnlyList<StallDto>> GetStallsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct);

    /// <summary>The same register a page at a time, for facilities too large to render at once.</summary>
    Task<CursorPagedResult<StallDto>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section, DateTime? cursor, int pageSize, CancellationToken ct);

    /// <summary>Who holds each space, optionally narrowed by section or searched by name or number.</summary>
    Task<StallHoldersListDto> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct);

    /// <summary>
    /// How each section of a facility stands for one month — the summary strip above the register. Month-scoped because a
    /// section's figures are an answer about a period, not a running total.
    /// </summary>
    Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
}
