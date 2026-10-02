namespace EEMOCantilanSDS.Testing;

/// <summary>
/// V3 utility semantics on the Web client: ECF and WCF are broader utility operations settled against a direct approved
/// amount. Meter readings, consumption and per-unit rates are not required financial inputs for current workflows, and
/// utilities are never presented as components of NPM stall rent.
/// </summary>
/// <remarks>
/// Checked on the .razor text, as the other page guards in this folder are: the rule is about what the office is shown.
/// Historical reading evidence may remain in the per-stall history drawer, which labels it as such.
/// </remarks>
public class UtilityDirectApprovedPresentationTests
{
    private static readonly string[] UtilityPages =
    [
        Path.Combine("Menus", "ElectricityConsumptionFees.razor"),
        Path.Combine("Menus", "ElectricityConsumptionFeesAccounts.razor"),
        Path.Combine("Menus", "ElectricityConsumptionFeesReport.razor"),
        Path.Combine("Menus", "WaterConsumptionFees.razor"),
        Path.Combine("Menus", "WaterConsumptionFeesAccounts.razor"),
        Path.Combine("Menus", "WaterConsumptionFeesReport.razor"),
    ];

    [Theory]
    [MemberData(nameof(Pages))]
    public void UtilityOperationPages_ShowNoMeterReadingConsumptionOrRate(string page)
    {
        var text = Read(page);

        foreach (var term in new[] { "kWh", "m³", "cu.m", "RatePerKwh", "RatePerCubicMeter", "PreviousReading", "CurrentReading", "Meter reading" })
            Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        Assert.Contains("Approved amount", text);
    }

    public static IEnumerable<object[]> Pages() => UtilityPages.Select(p => new object[] { p });

    [Fact]
    public void NpmReports_OffersRelatedUtilities_NotReadingBasedBilling()
    {
        var text = Read(Path.Combine("Reports", "NpmReports.razor"));

        // The utility statement view is reached from the Financial Report's deep link (?view=utilities); V3 removed its
        // separate "Related utilities" tab, but the view itself still exists and still states the two utilities apart.
        Assert.Contains("string.Equals(View, \"utilities\"", text);
        Assert.DoesNotContain("Utility Billing</span>", text);
        Assert.DoesNotContain("CONSUMPTION BILLING", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Generate Billing Statement", text);
        Assert.DoesNotContain("Electricity Reading", text);
        Assert.DoesNotContain("Water Reading", text);
        // The two utilities keep their own instruments.
        Assert.Contains("Electricity Consumption Fee (ECF)", text);
        Assert.Contains("Water Consumption Fee (WCF)", text);
        // Old readings survive only as labelled historical evidence.
        Assert.Contains("historical reading evidence", text);
    }

    [Fact]
    public void AddVendor_DoesNotPresentUtilitiesAsRentalCharges()
    {
        var text = Read(Path.Combine("Shared", "AddVendorModal.razor"));

        Assert.DoesNotContain("amount varies per reading", text);
        Assert.DoesNotContain(">Utility Charges<", text);
        Assert.Contains("Utility service", text);
        Assert.Contains("not a rental charge", text);
    }

    [Fact]
    public void ANewUtilityBill_DefaultsToTheDirectApprovedAmount()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Modals", "UtilityBillModal.razor"));

        Assert.Contains("ElecDirect = WaterDirect = true;", text);
        Assert.Contains("if (!s.Exists)", text);   // an existing bill keeps the basis it was recorded with
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", relative));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
