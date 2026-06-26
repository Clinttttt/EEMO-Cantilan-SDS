using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Reports;

/// <summary>
/// Per-facility collection report for the admin Export Data page (print / PDF / CSV). Composed from the
/// same canonical sources used elsewhere — per-facility stall compliance (rental facilities) and the
/// service-facility month records (SLH/TRM/TPM) — with the structured columns the printable tables need
/// (NPM market section, NCC area location, SLH animal/heads/rate, TRM trip/route, TPM goods). All money in PHP.
/// </summary>
public record CollectionReportDto(
    string PeriodLabel,
    DateOnly AsOf,
    IReadOnlyList<CollectionFacilityDto> Facilities
);

public record CollectionFacilityDto(
    FacilityCode Code,
    string Name,
    string Model,         // "Daily stall" | "Monthly rental" | "Per-head" | "Per-trip" | "Weekly market"
    bool IsRental,
    decimal Collected,
    decimal Outstanding,
    IReadOnlyList<CollectionRentalRowDto> Rentals,
    IReadOnlyList<CollectionTxnRowDto> Transactions
);

/// <summary>One rental compliance row. <see cref="Section"/> = NPM market section OR NCC area location
/// (empty for TCC/BBQ/ICE). <see cref="Coverage"/>/<see cref="CoverageBalance"/> are NPM-only (0 otherwise).
/// <see cref="FishKilos"/>/<see cref="FishFee"/> are the recognized fish volume and ₱1/kg fee for NPM
/// Fish Area stalls (0 elsewhere) — surfaced as a separate extra charge in the report.</summary>
public record CollectionRentalRowDto(
    string StallNo,
    string Section,
    string Occupant,
    decimal Rate,          // monthly rate (monthly facilities) or daily fee (NPM)
    string Status,
    decimal Collected,
    decimal Balance,
    string? ORNumber,
    decimal Coverage,
    decimal CoverageBalance,
    decimal FishKilos = 0m,
    decimal FishFee = 0m
);

/// <summary>One service-facility transaction row, columns vary by facility context.</summary>
public record CollectionTxnRowDto(
    string Payor,
    string Date,           // "MMM d"
    string Ref,            // TRM trip no. ("Trip #1"); empty otherwise
    string Detail,         // SLH animal type / TRM route / TPM goods
    int Heads,             // SLH heads (0 otherwise)
    decimal Rate,          // SLH rate per head (0 otherwise)
    string? ORNumber,
    decimal Amount
);
