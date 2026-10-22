using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>
/// Maps the existing NPM drawer's stable section VALUE, never the municipality's translated label.
/// Monthly and daily NPM use the same canonical MarketSection identity and custom-section registry.
/// </summary>
public static class MonthlyMarketSectionSelection
{
    public sealed record Result(MarketSection? Section, string? CustomSectionName, string? Error);

    public static Result Resolve(string? selection, string? newCustomName) =>
        selection switch
        {
            "Vegetable" => new(MarketSection.VegetableArea, null, null),
            "Fish" => new(MarketSection.FishSection, null, null),
            "Meat" => new(MarketSection.MeatSection, null, null),
            "__custom__" => ResolveCustom(newCustomName),
            { } existing when existing.StartsWith("existing:", StringComparison.Ordinal) =>
                ResolveCustom(existing["existing:".Length..]),
            _ => new(null, null, "Select a market section before registering the stall.")
        };

    private static Result ResolveCustom(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 60 || name.Any(char.IsControl))
            return new(null, null, "Enter a valid custom section name (1–60 characters).");
        return new(null, name, null);
    }
}
