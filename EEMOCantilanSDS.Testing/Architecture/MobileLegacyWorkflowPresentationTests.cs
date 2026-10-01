namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Presentation rules for the mature NPM, monthly-rental and Tabo collection pages, kept after their Mobile V3 restyle.
/// </summary>
/// <remarks>
/// These are MAUI pages: no test project renders them, so the rules are checked on the .razor text, as the other Mobile page
/// guards in this folder are. Each rule protects meaning, not pixels.
/// </remarks>
public class MobileLegacyWorkflowPresentationTests
{
    private static string Page(string name) => File.ReadAllText(Path.Combine(PagesDirectory(), name));

    [Fact]
    public void The_market_page_shows_no_meter_or_consumption_wording_for_utilities()
    {
        var market = Page("Market.razor");

        foreach (var term in new[] { "m³", "kWh", "cubic", "RatePerCubicMeter", "PreviousReading", "CurrentReading", "Water Consumption" })
            Assert.DoesNotContain(term, market, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tabo_names_its_own_vendor_fee_and_never_the_Fish_or_Meat_vendor_fee()
    {
        var tabo = Page("Taboan.razor");

        Assert.Contains("Tabo vendor fee", tabo);
        Assert.DoesNotContain("Fish", tabo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Meat", tabo, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Market.razor", "New Public Market")]
    [InlineData("Taboan.razor", "Tabo-an Public Market")]
    [InlineData("Taboan.razor", "Tabo-an Market")]
    public void Facility_names_come_from_the_server_menu_not_markup(string page, string hardCoded) =>
        Assert.DoesNotContain(hardCoded, Page(page));

    [Fact]
    public void The_monthly_sheet_shows_the_server_obligation_paid_and_remaining_and_is_labelled_monthly()
    {
        var monthly = Page("MonthlyCollection.razor");

        Assert.Contains("SelectedStall.MonthlyRate", monthly);
        Assert.Contains("SelectedStall.AmountPaid", monthly);
        Assert.Contains("SelectedStall.Balance", monthly);
        Assert.Contains("Monthly billing", monthly);
        Assert.DoesNotContain("Daily billing", monthly, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Market.razor")]
    [InlineData("MonthlyCollection.razor")]
    [InlineData("Taboan.razor")]
    public void The_on_device_acknowledgement_never_claims_to_be_the_Official_Receipt(string page)
    {
        var text = Page(page);

        Assert.Contains("does not replace the pre-numbered Official Receipt", text);
        Assert.DoesNotContain("Keep for your records", text);
    }

    [Fact]
    public void The_market_header_uses_the_server_business_date()
    {
        var market = Page("Market.razor");

        Assert.Contains("Session.Menu?.Today", market);
        Assert.Contains("@Today.ToString(", market);
    }

    private static string PagesDirectory() => Path.Combine(
        RepositoryRoot(), "EEMOCantilanSDS.Mobile", "Components", "Pages", "Menus");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
