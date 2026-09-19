using System.IO;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Pins the Vendor Registry host contract. The shared modal already tests its own PureDays rendering; these assertions
/// make the second host carry the tenant basis and rate context instead of silently falling back to RentGoal defaults.
/// </summary>
public class VendorRegistryPureDaysContractTests
{
    private static string VendorSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName,
            "EEMOCantilanSDS.Client",
            "Components",
            "Pages",
            "Menus",
            "Vendor.razor"));
    }

    [Fact]
    public void HostPassesTheExplicitBasisAndTenantRateContext()
    {
        var source = VendorSource();

        Assert.Contains("MonthBasis=\"@_npmMonthBasis\"", source);
        Assert.Contains("NpmDailyRate=\"@SelectedNpmDailyRate\"", source);
        Assert.Contains("NpmMonthlyRentInUse=\"@_npmMonthlyRentInUse\"", source);
        Assert.Contains("NpmFishRate=\"@_npmFishRate\"", source);
        Assert.Contains("SectionStatesItsOwnRate=\"@(SelectedSectionStatedRate > 0m)\"", source);
    }

    [Fact]
    public void PureDaysDoesNotRequireOrSynthesizeAMonthlyRent()
    {
        var source = VendorSource();

        Assert.Contains("_npmMonthBasis == NpmMonthBasis.RentGoal && Form.MonthlyRate <= 0", source);
        Assert.Contains("Form.FacilityCode != \"NPM\" || _npmMonthBasis == NpmMonthBasis.RentGoal", source);
    }

    [Fact]
    public void AConfiguredSectionRateRemainsAuthoritativeOnCreate()
    {
        var source = VendorSource();

        Assert.Contains("SelectedSectionStatedRate > 0m ? null : _npmDailyRate", source);
        Assert.Contains("DetailVendor.ResolvedDailyFee is { } resolvedFee", source);
        Assert.Contains("DailyRate=\"@(DetailVendor.ResolvedDailyFee", source);
        Assert.Contains("private decimal _npmDailyRate;", source);
        Assert.DoesNotContain("private decimal _npmDailyRate = 30m;", source);
        Assert.Contains("result.Value.VegetableAreaDailyRate", source);
        Assert.Contains("result.Value.FishSectionDailyRate", source);
        Assert.Contains("result.Value.MeatSectionDailyRate", source);
    }
}
