namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record StallComplianceDto(
    string StallNo,
    string Occupant,
    string ContractName,
    string Section,
    decimal MonthlyRate,
    decimal DailyRate,
    string Status,
    decimal AmountPaid,
    decimal Balance,
    string? ORNumber,
    int MissedMonths,
    double AreaSqm
);
