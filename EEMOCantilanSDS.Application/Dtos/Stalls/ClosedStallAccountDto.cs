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
    string? ClosedBy,
    /// <summary>Market section (NPM) or area location, using the tenant's own label; empty for facilities
    /// that have no sections. Lets the register be filtered and printed one section at a time.</summary>
    string Section = "",
    /// <summary>
    /// The day this lessee actually stopped holding the stall. Equals the contract's expiry when the term simply
    /// ran out; earlier when the occupancy was ended — handed to the next lessee, or frozen by closure.
    /// </summary>
    DateOnly? OccupancyEndedOn = null,
    /// <summary>
    /// True when the stall is now held by SOMEBODY ELSE. Such a row is history: it must be readable and printable,
    /// but never actionable — renewing or reopening it would act on the stall the incoming lessee occupies.
    /// </summary>
    bool StallReLet = false,
    /// <summary>
    /// The term this row is the record of. A re-let stall carries several, so anything acting on THIS lessee — such
    /// as placing them in a stall of their own when they return — must name the term rather than the stall, or it
    /// would read the sitting lessee's details instead.
    /// </summary>
    Guid ContractId = default,
    /// <summary>The space as currently measured, so a renewal can be checked against the record and corrected.</summary>
    double? AreaSqm = null,
    /// <summary>The office's note on the space (e.g. "Corner", "Extension"), shown alongside the area on renewal.</summary>
    string? AreaNote = null
);
