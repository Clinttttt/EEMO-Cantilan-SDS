namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>The month's NPM electricity &amp; water bills a collector can settle in the field.</summary>
public record MobileNpmUtilityDto(
    int Year,
    int Month,
    IReadOnlyList<MobileUtilityBillDto> Bills);

/// <summary>One computed utility bill for mobile collection (electricity + water settled independently).</summary>
public record MobileUtilityBillDto(
    Guid BillId,
    string StallNo,
    string Occupant,
    string Section,
    decimal ElecCharge,
    string ElecStatus,
    decimal ElecBalanceDue,
    decimal WaterCharge,
    string WaterStatus,
    decimal WaterBalanceDue,
    decimal TotalCharge,
    decimal AmountPaid,
    decimal BalanceDue,
    string? ElecORNumber,
    string? WaterORNumber);
