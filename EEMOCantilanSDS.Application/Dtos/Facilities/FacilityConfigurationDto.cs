using EEMOCantilanSDS.Domain.Constants;
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
    IReadOnlyList<ConfiguredRateDto> Rates,
    string? VegetableSectionLabel = null,
    string? FishSectionLabel = null,
    string? MeatSectionLabel = null);

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
        // Per-area daily rates, for an office that prices the areas of its market apart. An office that states none is
        // billed its market rate for every area.
        FeeRateKey.NpmDailyStallVegetable => "Daily stall fee — vegetable area",
        FeeRateKey.NpmDailyStallFish => "Daily stall fee — fish section",
        FeeRateKey.NpmDailyStallMeat => "Daily stall fee — meat section",
        // The month-length convention, taken from the rule rather than written out: an LGU that states no monthly rent
        // has its month read as this many daily fees, and a label saying "30" while the rule said otherwise would be
        // the office's own screen contradicting the arithmetic behind it.
        FeeRateKey.NpmMonthlyStall => $"Monthly stall rent (blank = {DomainRules.DailyBilledMonthDays} × daily)",
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
