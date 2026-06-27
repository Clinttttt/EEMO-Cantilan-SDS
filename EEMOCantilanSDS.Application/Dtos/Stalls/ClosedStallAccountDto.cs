using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <summary>
/// One inactive stall account for the Closed/Inactive Accounts register. Covers both explicitly
/// CLOSED (frozen) stalls and EXPIRED ones (contract term lapsed). Collected money is shown in full
/// (a closure/expiry never erases history); <see cref="Uncollected"/> is the arrears that had accrued
/// up to the end point (close date for closed, contract expiry for expired), excused/absent-aware.
/// </summary>
public sealed record ClosedStallAccountDto(
    Guid StallId,
    InactiveAccountState State,
    FacilityCode FacilityCode,
    string FacilityName,
    string StallNo,
    string Occupant,
    string? ContractName,
    DateOnly EffectivityDate,
    int DurationYears,
    decimal MonthlyRate,
    DateOnly? ClosedOn,
    DateOnly ExpiryDate,
    decimal LifetimeCollected,
    decimal Uncollected,
    string? ClosedBy
);
