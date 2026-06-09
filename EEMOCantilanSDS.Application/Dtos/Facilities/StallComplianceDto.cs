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
    int DurationYears
);
