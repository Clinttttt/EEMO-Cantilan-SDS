using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Vendors;

public sealed record VendorListItemDto(
    Guid StallId,
    string StallNo,
    string ActualOccupant,
    string? NameOnContract,
    string? ORNumber,
    FacilityCode FacilityCode,
    string FacilityName,
    MarketSection? Section,
    string? SectionDisplay,
    NccAreaLocation? AreaLocation,
    string? AreaLocationDisplay,
    decimal MonthlyRate,
    StallStatus Status,
    PaymentStatus PaymentStatus,
    DateTime? ContractDate,
    int ContractYears,
    double? AreaSqm,
    string? AreaNote,

    /// <summary>
    /// Whether this space is actually billed for electricity / water. The edit form must offer only the utilities the
    /// space has: showing them ticked for every market stall invites a clerk to save a meter that does not exist onto
    /// a payor who has never been billed for one.
    /// </summary>
    bool HasElectricity = false,
    bool HasWater = false,

    /// <summary>How the space is held; a space let without a signed contract has no term or leasee name.</summary>
    OccupancyArrangement Arrangement = OccupancyArrangement.SignedContract,

    /// <summary>
    /// What this stall IS billed for a day, settled by <c>NpmDailyFee</c>: its own rate, then its section's, then its
    /// area's, then the market's. Null where the space is not billed by the day. The detail card stated the MARKET's
    /// figure, so a stall in an area or a section the office prices apart was described at a rate it is not charged.
    /// </summary>
    decimal? ResolvedDailyFee = null,

    /// <summary>
    /// True where this stall sits in one of the office's own market sections that the office has CLOSED.
    /// </summary>
    /// <remarks>
    /// Such a stall must not be reopened from this list. Reopening it would start billing a space the market page does not
    /// show and no form offers, so the arrears would accrue where nobody can see them. Reopening the SECTION returns every
    /// stall its closure closed. The server refuses it as well - a disabled button is not a guard - and the register on the
    /// Closed Accounts page carries the same rule.
    /// </remarks>
    bool SectionClosed = false
);
