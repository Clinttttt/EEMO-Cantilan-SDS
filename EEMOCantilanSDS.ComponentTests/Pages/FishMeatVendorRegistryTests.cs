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
}
