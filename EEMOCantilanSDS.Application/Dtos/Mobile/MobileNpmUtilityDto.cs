namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// The NPM electricity &amp; water bills a collector can settle in the field: the asked month's bills, and any
/// earlier bill still owed — an unpaid bill stays collectible after the month turns over.
/// </summary>
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
    string? WaterORNumber,
    /// <summary>The month this bill is for — a bill from an earlier month must be named as such on the receipt
    /// and on screen, or the collector cannot tell which period they are settling.</summary>
    int BillingYear = 0,
    int BillingMonth = 0,
    /// <summary>The billing month as the office writes it, e.g. "July 2026".</summary>
    string PeriodLabel = "");
