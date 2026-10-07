using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Fish / Meat working registry: one primary table of the current year's active vendors with what each paid this month (the server's figure),
/// the Kanmanggay hero and toolbar, no tax-year control, and a two-column registration drawer.</summary>
public sealed class FishMeatVendorRegistryTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficeSourcesApiClient> _office = new();
    private static readonly int Year = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year;
    private static readonly int Month = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Month;

    internal static VendorRegistrationManagementRow Row(string name, FishMeatVendorType type, VendorRegistrationKind kind, string? business = null, string? address = null,
        string? reference = null, decimal fee = 0m, decimal weighing = 0m, VendorRegistrationStatus status = VendorRegistrationStatus.Active, int? year = null, Guid? prior = null) =>
        new(new(Guid.NewGuid(), year ?? Year, type, kind, name, business, address, reference), status, "head", new DateTime(2026, 1, 5, 1, 0, 0, DateTimeKind.Utc),
            status == VendorRegistrationStatus.Closed ? new DateOnly(2026, 6, 1) : null, null, null, null, prior, fee, weighing);

    public FishMeatVendorRegistryTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_office.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _office.Setup(x => x.ManageRegistrationsAsync(Year, Month, VendorRegistrationStatus.Active, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(
            new(Year, Year, Month,
            [
                Row("Lisa Ilogans", FishMeatVendorType.Fish, VendorRegistrationKind.New, "Ilogans Fish Stall", "Purok 3", "REG-1", fee: 120m, weighing: 66m),
                Row("Pantom Dant", FishMeatVendorType.Meat, VendorRegistrationKind.Renew)
            ], 75m)));
    }

    [Fact]
    public void TheRegistryIsOneTable_WithTheServersMonthlyTotal_AndNoSecondCollectionsTable()
    {
        var cut = RenderComponent<FishMeatVendorFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("table"));
            Assert.Equal(["Vendor", "Type", "Registration", "Address", "Collected this month"], cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ToArray());
            var rows = cut.FindAll("tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Lisa Ilogans") && r.Contains("Ilogans Fish Stall") && r.Contains("Fish") && r.Contains("New") && r.Contains("Purok 3") && r.Contains("₱186.00"));
            Assert.Contains(rows, r => r.Contains("Pantom Dant") && r.Contains("Meat") && r.Contains("Renew") && r.Contains("₱0.00"));
            Assert.Contains("num", cut.FindAll("tbody tr")[0].QuerySelectorAll("td").Last().ClassName);                              // right-aligned money
            foreach (var word in new[] { "NPM", "Payor", "Stall rent", "Collections this month", "Walk-up" })
                Assert.DoesNotContain(word, cut.Markup);
        }, Timeout);
        _office.Verify(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>()), Times.Never);       // no second activity read
    }

    [Fact]
    public void TheMainPageHasNoTaxYearControl_AndManageIsOneClickAway()
    {
        var cut = RenderComponent<FishMeatVendorFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".fh-dd, select"));
            Assert.DoesNotContain("Tax year", cut.Find(".toolbar-unified").TextContent);
            Assert.DoesNotContain("Tax year", cut.Find(".facility-hero").TextContent);
            var manage = cut.Find(".v3-panel-header a");
            Assert.Equal("Manage", manage.TextContent.Trim());
            Assert.Equal("/operations/fish-meat-vendor-fees/manage", manage.GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void ItUsesTheKanmanggayStructure_HeroFiguresOneToolbarFiltersAndTheOfficeActions()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Pantom Dant", cut.Markup), Timeout);

        Assert.Contains("OPERATION · FISH / MEAT", cut.Find(".facility-hero-eyebrow").TextContent);
        var figures = cut.FindAll(".facility-hero-stat").Select(s => s.QuerySelector(".facility-hero-val")!.TextContent.Trim() + "|" + s.QuerySelector(".facility-hero-key")!.TextContent.Trim()).ToList();
        Assert.Equal(["2|Registered", "1|Fish", "1|Meat"], figures);
        Assert.Single(cut.FindAll(".toolbar-unified"));
        Assert.Equal(["All", "Fish", "Meat"], cut.FindAll(".filter-tab").Select(t => t.TextContent.Trim()).ToArray());
        Assert.Equal(["Collection Activity", "Reports", "Import list", "Register vendor"], cut.FindAll(".fmv-actions-bar a, .fmv-actions-bar button").Select(a => a.TextContent.Trim()).ToArray());
        Assert.Equal("/operations/fish-meat-vendor-fees/import", cut.FindAll(".fmv-actions-bar a").Single(a => a.TextContent.Trim() == "Import list").GetAttribute("href"));
    }

    [Fact]
    public void TheFishAndMeatFiltersAndTheSearchNarrowTheRegistry_IncludingAddressAndReference()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Pantom Dant", cut.Markup), Timeout);

        cut.FindAll(".filter-tab").Single(t => t.TextContent.Trim() == "Meat").Click();
        Assert.Single(cut.FindAll("tbody tr"));
        cut.FindAll(".filter-tab").Single(t => t.TextContent.Trim() == "All").Click();

        cut.Find("input.search-input").Input("purok 3");                                              // an address the server holds
        Assert.Contains("Lisa Ilogans", Assert.Single(cut.FindAll("tbody tr")).TextContent);
        Assert.DoesNotContain("reference", cut.Find("input.search-input").GetAttribute("placeholder"), StringComparison.OrdinalIgnoreCase);
        cut.Find("input.search-input").Input("no such vendor");
        Assert.Contains("No vendor matches the search", cut.Markup);
    }

    [Fact]
    public void TheRegisterDrawerIsTheSharedKeepMountedDrawer_WithTwoColumnRows_AndNoBusinessPayorOrFeeConcepts()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor"), Timeout);

        Assert.NotNull(cut.Find(".eemo-drawer.eemo-drawer-right"));                                  // mounted while closed, so it can slide in
        Assert.DoesNotContain("show", cut.Find(".eemo-drawer").ClassName.Split(' '));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor").Click();

        Assert.Contains("show", cut.Find(".eemo-drawer").ClassName.Split(' '));
        var form = cut.Find("form[aria-label='Register vendor']");
        Assert.Empty(form.QuerySelectorAll(".avm-row-2"));
        Assert.Equal(["Vendor name", "Business name", "Type", "Registration", "Address"],
            form.QuerySelectorAll(":scope > .avm-group > label").Select(l => l.TextContent.Replace("*", "").Trim()).ToArray());
        Assert.DoesNotContain("Reference", form.TextContent);
        Assert.Contains($"Tax year {Year}", cut.Find(".eemo-drawer").TextContent);
        Assert.Empty(form.QuerySelectorAll("select"));                                                 // no native dropdown
        Assert.DoesNotContain("optional", form.TextContent, StringComparison.OrdinalIgnoreCase);
        foreach (var word in new[] { "Payor", "NPM", "stall", "fee amount", "Weight" })
            Assert.DoesNotContain(word, form.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Cancel", cut.Find(".eemo-drawer-footer .btn-ghost").TextContent.Trim());
        Assert.Equal("Register", cut.Find(".eemo-drawer-footer .btn-primary").TextContent.Trim());
    }

    [Fact]
    public void TheHeadRegistersOneVendor_AsFishOrMeat_ForTheCurrentYear_AndEscapeClosesTheDrawer()
    {
        RegisterFishMeatVendorRequest? sent = null;
        _office.Setup(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()))
            .Callback<RegisterFishMeatVendorRequest>(r => sent = r)
            .ReturnsAsync((RegisterFishMeatVendorRequest r) => Result<FishMeatVendorRegistrationDto>.Success(
                new(Guid.NewGuid(), r.TaxYear, r.VendorType, r.RegistrationKind, r.DisplayName, r.BusinessName, r.Address, r.Reference)));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor").Click();

        cut.Find("#fmv-register-form-name").Input("Lisa Ilogans");
        cut.Find("#fmv-register-form-address").Input("Purok 3");
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Trim() == "Meat").Click();
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Trim() == "Renew").Click();
        cut.Find("form[aria-label='Register vendor']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((Year, FishMeatVendorType.Meat, VendorRegistrationKind.Renew, "Lisa Ilogans", "Purok 3"), (sent!.TaxYear, sent.VendorType, sent.RegistrationKind, sent.DisplayName, sent.Address));
            Assert.Null(sent.Reference);
            Assert.Contains("Lisa Ilogans registered", cut.Markup);
        }, Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor").Click();
        cut.Find(".eemo-drawer").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.DoesNotContain("show", cut.Find(".eemo-drawer").ClassName.Split(' '));
    }

    [Fact]
    public void AnAdminReadsTheRegistry_ButCannotRegisterOrImport()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Register vendor");
        Assert.DoesNotContain(cut.FindAll("a"), a => a.TextContent.Trim() == "Import list");
        Assert.Contains(cut.FindAll("a"), a => a.TextContent.Trim() == "Manage");
    }

    [Fact]
    public void AFailedRegistryLoad_SaysSo_AndRetries()
    {
        _office.Setup(x => x.ManageRegistrationsAsync(Year, Month, VendorRegistrationStatus.Active, null)).ReturnsAsync(Result<VendorRegistryManagement>.Failure("boom"));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("couldn't be loaded", cut.Find("[role=alert]").TextContent), Timeout);
        Assert.Equal("Try again", cut.Find("[role=alert] button").TextContent.Trim());
    }
}
