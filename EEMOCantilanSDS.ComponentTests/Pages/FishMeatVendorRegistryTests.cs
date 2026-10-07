using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The independent Fish / Meat vendor registry: registered by year as Fish or Meat, with no NPM stall, Business Payor or fixed rate.</summary>
public sealed class FishMeatVendorRegistryTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficeSourcesApiClient> _office = new();
    private static readonly int Year = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year;

    public FishMeatVendorRegistryTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_office.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _office.Setup(x => x.RegistrationsAsync(Year)).ReturnsAsync(Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Success(
        [
            new(Guid.NewGuid(), Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Ana Reyes", "Reyes Fish Stall", null, "REG-1"),
            new(Guid.NewGuid(), Year, FishMeatVendorType.Meat, VendorRegistrationKind.Renew, "Pantom Dant", null, null, null)
        ]));
        _office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]));
    }

    [Fact]
    public void TheRegistryListsVendorsWithTypeAndRegistration_WithNoNpmPayorOrRateConcepts()
    {
        var cut = RenderComponent<FishMeatVendorFees>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("table")[0].QuerySelectorAll("tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Ana Reyes") && r.Contains("Reyes Fish Stall") && r.Contains("Fish") && r.Contains("New") && r.Contains("REG-1"));
            Assert.Contains(rows, r => r.Contains("Pantom Dant") && r.Contains("Meat") && r.Contains("Renew"));
            foreach (var word in new[] { "NPM", "Payor", "Stall rent", "₱900" })
                Assert.DoesNotContain(word, cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void TheHeadRegistersOneVendor_AsFishOrMeat_ForTheChosenYear()
    {
        RegisterFishMeatVendorRequest? sent = null;
        _office.Setup(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()))
            .Callback<RegisterFishMeatVendorRequest>(r => sent = r)
            .ReturnsAsync((RegisterFishMeatVendorRequest r) => Result<FishMeatVendorRegistrationDto>.Success(
                new(Guid.NewGuid(), r.TaxYear, r.VendorType, r.RegistrationKind, r.DisplayName, r.BusinessName, r.Address, r.Reference)));
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor").Click();

        cut.Find("#fmv-name").Input("Lisa Ilogans");
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Trim() == "Meat").Click();
        cut.FindAll("[role='radio']").Single(b => b.TextContent.Trim() == "Renew").Click();
        cut.Find("form[aria-label='Register vendor']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((Year, FishMeatVendorType.Meat, VendorRegistrationKind.Renew, "Lisa Ilogans"), (sent!.TaxYear, sent.VendorType, sent.RegistrationKind, sent.DisplayName));
            Assert.Contains("Lisa Ilogans registered", cut.Markup);
        }, Timeout);

    }

    [Fact]
    public void AnAdminReadsTheRegistry_ButCannotRegister()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Markup), Timeout);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Register vendor");
    }

    [Fact]
    public void ActivityShowsVendorFeeAndWeightMeasureWithTheirSrc_AndAWalkUpPayerNameStaysAsGiven()
    {
        var ana = Guid.NewGuid();
        _office.Setup(x => x.RegistrationsAsync(Year)).ReturnsAsync(Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Success(
            [new(ana, Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Ana Reyes", null, null, null)]));
        SourceNativeActivityDto Row(string op, string src, decimal amount, Guid? vendor, string? payer, decimal? kg) =>
            new(Guid.NewGuid(), src, DateOnly.FromDateTime(DateTime.Today), DateTime.UtcNow, op, null, vendor, payer, null, "Cora", amount, amount, "Posted", null, null, null, null, kg);
        _office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), CollectorOperationCodes.FishMeatVendorFee)).ReturnsAsync(
            Result<IReadOnlyList<SourceNativeActivityDto>>.Success([Row(CollectorOperationCodes.FishMeatVendorFee, "SRC-2026-000011", 120m, null, "Walk-up Payer", null)]));
        _office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), CollectorOperationCodes.WeightAndMeasure)).ReturnsAsync(
            Result<IReadOnlyList<SourceNativeActivityDto>>.Success([Row(CollectorOperationCodes.WeightAndMeasure, "SRC-2026-000012", 66m, ana, null, 3m)]));

        var cut = RenderComponent<FishMeatVendorFees>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("table")[1].QuerySelectorAll("tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("SRC-2026-000011") && r.Contains("Vendor Fee") && r.Contains("Walk-up Payer") && r.Contains("₱120.00"));
            Assert.Contains(rows, r => r.Contains("SRC-2026-000012") && r.Contains("Weight & Measure") && r.Contains("Ana Reyes") && r.Contains("3 kg") && r.Contains("₱66.00"));
        }, Timeout);
    }

    [Fact]
    public void ItUsesTheKanmanggayStructure_HeroFiguresOneToolbarFiltersAndTheOfficeActions()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Pantom Dant", cut.Markup), Timeout);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("OPERATION · FISH / MEAT", cut.Find(".facility-hero-eyebrow").TextContent);
            Assert.Contains($"Tax year {Year} · OR Official Receipt", cut.Find(".facility-hero-sub").TextContent);
            var figures = cut.FindAll(".facility-hero-stat").Select(s => s.QuerySelector(".facility-hero-val")!.TextContent.Trim() + "|" + s.QuerySelector(".facility-hero-key")!.TextContent.Trim()).ToList();
            Assert.Equal(3, figures.Count);
            Assert.Equal(["2|Registered", "1|Fish", "1|Meat"], figures);
            Assert.Single(cut.FindAll(".toolbar-unified"));                                            // one toolbar, no second floating row
            Assert.Equal(["All", "Fish", "Meat"], cut.FindAll(".filter-tab").Select(t => t.TextContent.Trim()).ToArray());
            var actions = cut.FindAll(".fmv-actions-bar a, .fmv-actions-bar button").Select(a => a.TextContent.Trim()).ToList();
            Assert.Equal(["Collection Activity", "Reports", "Import list", "Register vendor"], actions);
            Assert.Equal("/operations/fish-meat-vendor-fees/import", cut.FindAll(".fmv-actions-bar a").Single(a => a.TextContent.Trim() == "Import list").GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void TheFishAndMeatFiltersNarrowTheRegistry()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => Assert.Contains("Pantom Dant", cut.Markup), Timeout);

        cut.FindAll(".filter-tab").Single(t => t.TextContent.Trim() == "Meat").Click();

        var rows = cut.FindAll("table")[0].QuerySelectorAll("tbody tr").Select(r => r.TextContent).ToList();
        Assert.Single(rows); Assert.Contains("Pantom Dant", rows[0]);
    }

    [Fact]
    public void TheRegisterDrawerGroupsVendorRegistrationAndDetails_InTheSharedDrawer()
    {
        var cut = RenderComponent<FishMeatVendorFees>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register vendor").Click();

        foreach (var heading in new[] { "Vendor", "Registration", "Details" }) Assert.Contains(heading, cut.Find("form[aria-label='Register vendor']").TextContent);
        foreach (var word in new[] { "Payor", "NPM", "stall", "fee amount", "Weight" })
            Assert.DoesNotContain(word, cut.Find("form[aria-label='Register vendor']").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
