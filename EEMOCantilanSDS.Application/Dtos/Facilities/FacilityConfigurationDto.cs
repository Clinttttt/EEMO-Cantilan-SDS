using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// Read model for the in-portal Facility Configuration page: the current tenant's configured facilities
/// (with their billing model and fixed rates) plus the canonical facility types still available to add.
/// </summary>
public record FacilityConfigurationDto(
    IReadOnlyList<ConfiguredFacilityDto> Configured,
    IReadOnlyList<AvailableFacilityDto> Available);

public record ConfiguredFacilityDto(
    string Code,
    string Name,
    string ShortName,
    string? Description,
    string BillingModel,
    bool IsActive,
    int StallCount,
    IReadOnlyList<ConfiguredRateDto> Rates);

/// <summary>
/// A configurable fixed rate for a facility: the current effective amount (a customised row, else the
/// ordinance default), with the key name so the UI can send an edit, and whether it is customised.
/// </summary>
public record ConfiguredRateDto(string Key, string Label, decimal Amount, bool IsCustom);

public record AvailableFacilityDto(
    string Code,
    string Name,
    string ShortName,
    string BillingModel,
    bool IsCustom = false);

/// <summary>
/// Presentation helpers shared by the query handler and repository so a billing archetype or rate key is
/// humanised in exactly one place (never a bare enum name in the UI).
/// </summary>
public static class FacilityDisplay
{
    public static string BillingModel(BillingArchetype archetype) => archetype switch
    {
        BillingArchetype.DailyStall => "Daily stall rental",
        BillingArchetype.MonthlyRental => "Monthly rental",
        BillingArchetype.WeeklyMarket => "Weekly market (per vendor)",
        BillingArchetype.PerTrip => "Per trip",
        BillingArchetype.PerHead => "Per head",
        _ => "Custom",
    };

    public static string RateLabel(FeeRateKey key) => key switch
    {
        FeeRateKey.NpmDailyStall => "Daily stall fee",
        FeeRateKey.NpmFishPerKilo => "Fish fee (per kilo)",
        FeeRateKey.SlhHogPerHead => "Hog (per head)",
        FeeRateKey.SlhLargePerHead => "Large animal (per head)",
        FeeRateKey.TpmVendorDay => "Vendor (per market day)",
        FeeRateKey.TrmPerTrip => "Per trip",
        FeeRateKey.ElecPerKwh => "Electricity (per kWh)",
        FeeRateKey.WaterPerCubicMeter => "Water (per m³)",
        _ => key.ToString(),
    };
}
