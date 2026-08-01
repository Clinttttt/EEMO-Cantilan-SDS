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
    OccupancyArrangement Arrangement = OccupancyArrangement.SignedContract
);
