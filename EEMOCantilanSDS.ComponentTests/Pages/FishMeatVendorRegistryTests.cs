using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
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
    private readonly Mock<IFishMeatPayorFollowUpApiClient> _followUp = new();
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
        Services.AddSingleton(_followUp.Object);
        _followUp.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<FishMeatPayorFollowUpPageDto>.Success(new([], 0)));
        JSInterop.Mode = JSRuntimeMode.Loose;
        _office.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 3, [])));
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
            foreach (var word in new[] { "Payor", "Stall rent", "Collections this month", "Walk-up" })
                Assert.DoesNotContain(word, cut.Find("[aria-label='Registered vendors']").TextContent);
        }, Timeout);
        _office.Verify(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>()), Times.Never);       // no second activity read
    }

    [Fact]
    public void TheMainPageHasNoTaxYearControl_AndManageIsOneClickAway()
    {
        var cut = RenderComponent<FishMeatVendorFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".toolbar-unified .fh-dd, .toolbar-unified select"));
            Assert.DoesNotContain("Tax year", cut.Find(".toolbar-unified").TextContent);
            Assert.DoesNotContain("Tax year", cut.Find(".v3h").TextContent);
            var manage = cut.Find(".fmv-toolbar a[href$='/manage']");
            Assert.Equal("Manage", manage.TextContent.Trim());
            Assert.Equal("/operations/fish-meat-vendor-fees/manage", manage.GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void ItUsesTheKanmanggayStructure_HeroFiguresOneToolbarFiltersAndTheOfficeActions()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Pantom Dant", cut.Markup), Timeout);

        Assert.Contains("Fish / Meat", cut.Find(".v3h-eyebrow").TextContent);
        var figures = cut.FindAll(".v3h-figure").Select(s => s.QuerySelector(".v3h-val")!.TextContent.Trim() + "|" + s.QuerySelector(".v3h-key")!.TextContent.Trim()).ToList();
        Assert.Equal(["2|Registered", "1|Fish", "1|Meat"], figures);
        Assert.Single(cut.FindAll(".toolbar-unified"));
        Assert.Equal(["All", "Fish", "Meat"], cut.FindAll(".filter-tab").Select(t => t.TextContent.Trim()).ToArray());
        Assert.Equal(["All", "Fish", "Meat", "Vendors 2", "Needs reg. 0", "NPM links 3", "Manage", "Reports", "Office register", "Register vendor"], cut.FindAll(".fmv-actions-bar a, .fmv-actions-bar button").Select(a => a.TextContent.Trim()).ToArray());
        Assert.Empty(cut.FindAll(".fmv-toolbar a[href='/collections/activity'], .fmv-toolbar a[href$='/import']"));
        Assert.Empty(cut.FindAll(".v3-panel-header button, .v3-panel-header a"));
        Assert.NotNull(cut.Find("button[title='Payors requiring vendor registration']"));
        Assert.NotNull(cut.Find("button[title='Review Fish/Meat occupants from NPM']"));
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
        Assert.Equal(2, form.QuerySelectorAll(".vendor-form-section").Length);
        Assert.Equal(["Vendor name", "Business name", "Last name", "First name", "Middle name", "Name extension"],
            form.QuerySelectorAll(".vendor-form-section:first-of-type .avm-group > label").Select(l => l.TextContent.Replace("*", "").Trim()).ToArray());
        Assert.Equal("true", form.QuerySelector("input[id$='-name']")!.GetAttribute("aria-required"));
        Assert.DoesNotContain("Reference", form.TextContent);
        Assert.Contains($"Tax year {Year}", cut.Find(".eemo-drawer").TextContent);
        Assert.Single(form.QuerySelectorAll(".vendor-form-section select")); // the optional office sex record
        Assert.Contains("Nature of business", form.TextContent);
        Assert.Contains("Last name", form.TextContent);
        Assert.Contains("optional", form.TextContent);
        foreach (var word in new[] { "Payor", "NPM", "stall", "fee amount", "Weight" })
            Assert.DoesNotContain(word, form.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Import list", "Cancel" }, cut.FindAll(".eemo-drawer-footer .btn-ghost").Select(x => x.TextContent.Trim()));
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

    private static FishMeatPayorFollowUpRowDto Payor(string name = "Giorgia Macapagal", decimal amount = 43m) =>
        new(Guid.NewGuid(), "SRC-2026-000042", new DateOnly(2026, 10, 13), new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc), name, amount, "Pat Collector");

    private static FishMeatPayorFollowUpDetailDto PayorDetail(FishMeatPayorFollowUpRowDto row, params FishMeatPayorSuggestionDto[] suggestions) =>
        new(row, "OR 7700123", "OfficialReceipt", "Posted", 2026, suggestions, null, null, null);

    [Fact]
    public void ADirectPayorWhoIsOnlyANameAppearsUnderNeedsRegistration_NotInTheRegistry_AndItsAmountIsNotABalance()
    {
        var row = Payor();
        _followUp.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<FishMeatPayorFollowUpPageDto>.Success(new([row], 1)));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Needs reg. 1", cut.Find(".fmv-views").TextContent), Timeout);
        Assert.DoesNotContain("Giorgia Macapagal", cut.Markup);               // never listed as a registered vendor

        cut.FindAll(".fmv-view").Single(b => b.TextContent.StartsWith("Needs reg.")).Click();

        var table = cut.Find("table");
        Assert.Equal(["Payor", "Collection", "Amount paid", "Registration", "Action"], table.QuerySelectorAll("thead th").Select(h => h.TextContent.Trim()).ToArray());
        var text = table.QuerySelector("tbody tr")!.TextContent;
        Assert.Contains("Giorgia Macapagal", text); Assert.Contains("SRC-2026-000042", text); Assert.Contains("₱43.00", text); Assert.Contains("Needs registration", text);
        Assert.All(cut.FindAll(".filter-tab"), button => Assert.True(button.HasAttribute("disabled"))); // no guessed type on an unregistered payor
        Assert.Contains("already received", table.QuerySelector("caption")!.TextContent);
    }

    [Fact]
    public void ReviewOpensThePayorsCollection_AndRegisteringLinksItWithoutChangingTheAmount()
    {
        var row = Payor();
        _followUp.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<FishMeatPayorFollowUpPageDto>.Success(new([row], 1)));
        _followUp.Setup(x => x.DetailAsync(row.CollectionId)).ReturnsAsync(Result<FishMeatPayorFollowUpDetailDto>.Success(PayorDetail(row)));
        FishMeatPayorResolveRequest? sent = null;
        _followUp.Setup(x => x.ResolveAsync(It.IsAny<FishMeatPayorResolveRequest>())).Callback<FishMeatPayorResolveRequest>(r => sent = r)
            .ReturnsAsync(Result<FishMeatPayorResolveResult>.Success(new(row.CollectionId, new(Guid.NewGuid(), 2026, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Giorgia Macapagal", null, null, null), true)));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Needs reg. 1", cut.Find(".fmv-views").TextContent), Timeout);
        cut.FindAll(".fmv-view").Single(b => b.TextContent.StartsWith("Needs reg.")).Click();

        cut.Find("button[aria-label='Review Giorgia Macapagal, SRC-2026-000042']").Click();

        cut.WaitForAssertion(() => Assert.Contains("OR 7700123", cut.Find(".eemo-drawer.show").TextContent), Timeout);
        var drawer = cut.Find(".eemo-drawer.show").TextContent;
        Assert.Contains("₱43.00", drawer); Assert.Contains("already posted and counted", drawer);
        Assert.Equal("Giorgia Macapagal", cut.Find("#fmp-name").GetAttribute("value"));             // prefilled from what the Collector recorded
        Assert.True(cut.Find(".eemo-drawer-footer .btn-primary").HasAttribute("disabled"));         // the vendor type is never assumed
        cut.FindAll(".fmp-seg button").Single(b => b.TextContent == "Meat").Click();
        cut.Find("#fmp-address").Input("Stall 4");
        cut.Find(".eemo-drawer-footer .btn-primary").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal(row.CollectionId, sent!.CollectionId); Assert.Null(sent.VendorRegistrationId);
        Assert.Equal((FishMeatVendorType.Meat, "Giorgia Macapagal", "Stall 4"), (sent.Register!.VendorType, sent.Register.DisplayName, sent.Register.Address));
        cut.WaitForAssertion(() => Assert.Contains("registered for 2026. SRC-2026-000042 and its amount are unchanged", cut.Find(".fmv-saved").TextContent), Timeout);
    }

    [Fact]
    public void ASimilarNameIsOnlyASuggestion_UntilTheHeadPicksIt_ThenItLinksThatExistingVendor()
    {
        var row = Payor("Lisa Ilogans");
        var existing = new FishMeatVendorRegistrationDto(Guid.NewGuid(), 2026, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Lisa Ilogans", "Ilogans Fish Stall", null, null);
        _followUp.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<FishMeatPayorFollowUpPageDto>.Success(new([row], 1)));
        _followUp.Setup(x => x.DetailAsync(row.CollectionId)).ReturnsAsync(Result<FishMeatPayorFollowUpDetailDto>.Success(PayorDetail(row, new FishMeatPayorSuggestionDto(existing, "Similar name"))));
        FishMeatPayorResolveRequest? sent = null;
        _followUp.Setup(x => x.ResolveAsync(It.IsAny<FishMeatPayorResolveRequest>())).Callback<FishMeatPayorResolveRequest>(r => sent = r)
            .ReturnsAsync(Result<FishMeatPayorResolveResult>.Success(new(row.CollectionId, existing, false)));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Needs reg. 1", cut.Find(".fmv-views").TextContent), Timeout);
        cut.FindAll(".fmv-view").Single(b => b.TextContent.StartsWith("Needs reg.")).Click();
        cut.Find("button[aria-label='Review Lisa Ilogans, SRC-2026-000042']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".fmp-pick")), Timeout);

        Assert.Empty(cut.FindAll(".fmp-pick.is-picked"));                                            // nothing is chosen for the Head
        Assert.Equal("Register and link", cut.Find(".eemo-drawer-footer .btn-primary").TextContent.Trim());
        cut.Find(".fmp-pick").Click();
        Assert.Equal("Link to this vendor", cut.Find(".eemo-drawer-footer .btn-primary").TextContent.Trim());
        Assert.Empty(cut.FindAll("#fmp-name"));                                                      // linking shows no registration form
        cut.Find(".eemo-drawer-footer .btn-primary").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal(existing.Id, sent!.VendorRegistrationId); Assert.Null(sent.Register);
        cut.WaitForAssertion(() => Assert.Contains("linked for 2026", cut.Find(".fmv-saved").TextContent), Timeout);
    }

    [Fact]
    public void AnAdminSeesThePayorButCannotRegisterOrLink()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var row = Payor();
        _followUp.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<FishMeatPayorFollowUpPageDto>.Success(new([row], 1)));
        _followUp.Setup(x => x.DetailAsync(row.CollectionId)).ReturnsAsync(Result<FishMeatPayorFollowUpDetailDto>.Success(PayorDetail(row)));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Needs reg. 1", cut.Find(".fmv-views").TextContent), Timeout);
        cut.FindAll(".fmv-view").Single(b => b.TextContent.StartsWith("Needs reg.")).Click();
        cut.Find("button[aria-label='Review Giorgia Macapagal, SRC-2026-000042']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Only the Head can register", cut.Find(".eemo-drawer.show").TextContent), Timeout);
        Assert.Empty(cut.FindAll(".eemo-drawer.show .eemo-drawer-footer .btn-primary"));
        Assert.Empty(cut.FindAll("#fmp-name"));
    }

    [Fact]
    public void AFailedNeedsRegistrationLoad_IsNotAnEmptyList()
    {
        _followUp.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<FishMeatPayorFollowUpPageDto>.Failure("boom"));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Lisa Ilogans", cut.Markup), Timeout);
        Assert.Contains("Needs reg. —", cut.Find(".fmv-views").TextContent);
        cut.FindAll(".fmv-view").Single(b => b.TextContent.StartsWith("Needs reg.")).Click();
        Assert.Contains("couldn't be loaded", cut.Find("[role=alert]").TextContent);
        Assert.DoesNotContain("Every direct Fish / Meat payor has a registered vendor", cut.Markup);
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
