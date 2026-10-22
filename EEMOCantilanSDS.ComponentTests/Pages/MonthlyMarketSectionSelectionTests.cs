using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class MonthlyMarketSectionSelectionTests
{
    [Theory]
    [InlineData("Vegetable", MarketSection.VegetableArea)]
    [InlineData("Fish", MarketSection.FishSection)]
    [InlineData("Meat", MarketSection.MeatSection)]
    public void StandardSectionsRetainStableIdentity(string selection, MarketSection expected)
    {
        var result = MonthlyMarketSectionSelection.Resolve(selection, null);
        Assert.Equal(expected, result.Section);
        Assert.Null(result.CustomSectionName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void LocalExistingAndNewSectionsAreNotReclassifiedByDisplayName()
    {
        var existing = MonthlyMarketSectionSelection.Resolve("existing:Sari-Sari Area", null);
        Assert.Null(existing.Section);
        Assert.Equal("Sari-Sari Area", existing.CustomSectionName);
        Assert.Null(existing.Error);
        var fresh = MonthlyMarketSectionSelection.Resolve("__custom__", "  Dry Goods  ");
        Assert.Null(fresh.Section);
        Assert.Equal("Dry Goods", fresh.CustomSectionName);
        Assert.Null(fresh.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unrecognized")]
    [InlineData("existing:")]
    [InlineData("__custom__")]
    public void InvalidOrIncompleteSectionCannotSilentlyDefaultToVegetable(string? section)
    {
        var result = MonthlyMarketSectionSelection.Resolve(section, null);
        Assert.NotNull(result.Error);
        Assert.Null(result.Section);
        Assert.Null(result.CustomSectionName);
    }
}
