namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record StallComplianceDto(
    Guid StallId,
    string StallNo,
    string Occupant,
    string ContractName,
    string Section,
    string StallType,
    decimal MonthlyRate,
    decimal DailyRate,
    string Status,
    decimal AmountPaid,
    decimal Balance,
    string? ORNumber,
    int MissedMonths,
    double AreaSqm,
    DateOnly? EffectivityDate,
    int DurationYears,
    // The period rent obligation that has come due for this stall (the "expected bill"), excluding
    // utilities. For NPM this is occupancy-prorated: collectable days in the period × ₱30 (so a payor
    // who started mid-month owes only from their effectivity date), not the flat 30-day MonthlyRate.
    decimal ExpectedBill
);
