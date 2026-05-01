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
    PaymentStatus PaymentStatus
);
