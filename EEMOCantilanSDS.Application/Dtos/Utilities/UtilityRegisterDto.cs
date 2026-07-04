namespace EEMOCantilanSDS.Application.Dtos.Utilities;

/// <summary>
/// The end-of-month utility billing register: one row per active NPM stall for the period, with its
/// bill (if one has been recorded) and running totals for the summary cards.
/// </summary>
public record UtilityRegisterDto(
    int Year,
    int Month,
    decimal TotalDue,
    decimal TotalUnpaid,
    decimal TotalPaid,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int UnbilledCount,
    IReadOnlyList<UtilityRegisterRowDto> Rows);

/// <summary>One register line — an active NPM stall and its utility bill for the period (if any).</summary>
public record UtilityRegisterRowDto(
    Guid StallId,
    string StallNo,
    string Occupant,
    string Section,
    Guid? BillId,
    bool HasBill,
    decimal ElecPreviousReading,
    decimal ElecCurrentReading,
    decimal ElecConsumption,
    decimal ElecCharge,
    decimal WaterPreviousReading,
    decimal WaterCurrentReading,
    decimal WaterConsumption,
    decimal WaterCharge,
    decimal TotalCharge,
    string Status,          // "Paid" / "Partial" / "Unpaid" / "Unbilled" (overall)
    decimal BalanceDue,
    string ElecStatus,      // "Paid" / "Partial" / "Unpaid" / "Unbilled"
    string WaterStatus);
