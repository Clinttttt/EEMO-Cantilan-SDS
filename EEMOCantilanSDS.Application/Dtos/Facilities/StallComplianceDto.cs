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
    decimal ExpectedBill,
    // Count of excused/absent days for this stall within the selected period (NPM only; 0 elsewhere).
    // Surfaced so the admin sees WHY a payor owes less / shows the distinct "Absent" status.
    int AbsentDays = 0,
    // Fish kilos weighed for this stall over the period (NPM only; 0 elsewhere). Recorded on the daily collection
    // whichever way the stall settled, so it is summed independently of how the money was counted.
    decimal FishKilos = 0m,
    decimal? AccountAssessment = null,
    // Activated NPM month authority, net of approved as-of credits; distinct from earned ExpectedBill.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    decimal? FullPeriodRentObligation = null,
    // The fully elapsed unpaid months behind MissedMonths with what each still owes, in billing order. Filled only where the caller counts
    // a missed-month span (the delinquency source); null elsewhere. This is what lets each obligation be classified on its own.
    IReadOnlyList<UnpaidMonthDto>? UnpaidMonths = null,
    int? DurationMonths = null
);

/// <summary>One fully elapsed unpaid billing month of a stall account and what is still owed on it (partial payments already deducted).</summary>
public sealed record UnpaidMonthDto(int Year, int Month, decimal Unpaid);
