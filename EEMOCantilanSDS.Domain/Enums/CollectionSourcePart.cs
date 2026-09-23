namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// Distinguishes independently collected components from one source row.
/// DailyFee includes any MonthEndAdjustment; the adjustment is not a separate source part.
/// </summary>
public enum CollectionSourcePart
{
    Electricity = 1,
    Water = 2,
    DailyFee = 3,
    FishFee = 4
}
