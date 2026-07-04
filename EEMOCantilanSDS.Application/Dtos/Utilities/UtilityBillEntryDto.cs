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
    string? WaterORNumber);
