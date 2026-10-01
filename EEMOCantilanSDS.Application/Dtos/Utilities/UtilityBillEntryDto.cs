namespace EEMOCantilanSDS.Application.Dtos.Utilities;

/// <summary>
/// Seed for the utility entry modal. If a bill already exists for the month it is returned for editing
/// (<see cref="Exists"/> = true); otherwise the previous readings are carried forward from the stall's
/// most recent prior bill (its current readings) and the last rates are pre-filled for convenience.
/// </summary>
public record UtilityBillEntryDto(
    bool Exists,
    decimal ElecPreviousReading,
    decimal ElecCurrentReading,
    decimal ElecRatePerKwh,
    decimal WaterPreviousReading,
    decimal WaterCurrentReading,
    decimal WaterRatePerCubicMeter,
    string ElecStatus,
    decimal ElecPartialAmount,
    string WaterStatus,
    decimal WaterPartialAmount,
    string? ElecORNumber,
    string? WaterORNumber,
    // IA-050: a DirectApproved part is one approved amount (carried in the rate field) with no meter reading.
    string ElecCalculationBasis = "Metered",
    string WaterCalculationBasis = "Metered",
    // IA-055: the bases the server will accept for each part. ["DirectApproved"] for any part with no recorded metered
    // assessment (every new month); ["DirectApproved", "Metered"] only for a recorded metered part, whose readings may be
    // resubmitted unchanged or restated as a direct amount. Clients offer a reading entry only when "Metered" is listed.
    IReadOnlyList<string>? AllowedElecCalculationBases = null,
    IReadOnlyList<string>? AllowedWaterCalculationBases = null);
