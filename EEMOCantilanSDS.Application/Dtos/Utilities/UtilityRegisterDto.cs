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
    string WaterStatus,

    /// <summary>
    /// Whether the stall is billed for electricity / water (its <c>ApplicableFees</c> flags). The register
    /// lists only metered stalls, but a stall may be metered for ONE utility — the report must then show a
    /// reading and a status for that utility alone, instead of reporting the other as "Unbilled" forever.
    /// Default true so an older consumer keeps its previous behaviour.
    /// </summary>
    bool HasElectricity = true,
    bool HasWater = true,

    /// <summary>
    /// The rates these charges were computed at, carried so a billing statement can show its own arithmetic:
    /// consumption × rate = charge. A statement that states an amount without the rate behind it cannot be checked
    /// by the payor it is handed to, and being checkable is the point of issuing one.
    ///
    /// <para>
    /// Taken from the BILL rather than from the facility's current rate: a bill raised before a rate changed must go
    /// on stating the rate it was actually charged at, or a reissued statement would contradict the receipt. Zero for
    /// a stall with no bill for the period.
    /// </para>
    /// </summary>
    decimal ElecRatePerKwh = 0m,
    decimal WaterRatePerCubicMeter = 0m);
