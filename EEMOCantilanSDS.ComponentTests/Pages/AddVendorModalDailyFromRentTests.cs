using EEMOCantilanSDS.Domain.Enums;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// The form working a daily fee out from the monthly rent, and the one case where it must not.
///
/// <para>
/// The office asked for it in its own words: type ₱900 a month and the daily fee should read ₱30, because that is how its
/// ordinance reads a daily-collected space - let for a month, collected in thirty installments.
/// </para>
/// <para>
/// The case that must not fire is the reason these exist. A figure in a custom section stall's daily field becomes that
/// stall's OWN rate, and an own rate outranks its section's stated fee for ever. So where the office has priced the
/// section, the form opens that field BLANK and it has to stay blank, however much rent is typed above it.
/// </para>
/// </summary>
public class AddVendorModalDailyFromRentTests : TestContext
{
    private IRenderedComponent<AddVendorModal> RenderForm(AddVendorModal.VendorModalForm form, bool isEditing = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());

        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.IsEditing, isEditing)
            .Add(c => c.Form, form)
            .Add(c => c.NpmDailyRate, 30m)
            .Add(c => c.NpmFishRate, 1m));
    }

    /// <summary>The same form for an office that bills the days a month has.</summary>
    private IRenderedComponent<AddVendorModal> RenderFormOnPureDays(AddVendorModal.VendorModalForm form)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());

        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.IsEditing, false)
            .Add(c => c.Form, form)
            .Add(c => c.NpmDailyRate, 30m)
            .Add(c => c.NpmFishRate, 1m)
            .Add(c => c.MonthBasis, NpmMonthBasis.PureDays));
    }

    [Fact]
    public void OnPureDaysTheFormAsksForNoMonthlyAmountAtAll()
    {
        // A month owes the days it has on that basis, so no two months owe the same and a monthly figure is a number no
        // month actually owes. Dropped rather than shown and ignored: a form that collects something nothing reads teaches
        // a clerk the screen is decorative.
        var cut = RenderFormOnPureDays(CustomAreaStall(dailyOnOpen: 30m));

        Assert.Empty(cut.FindAll("input.avm-input[placeholder='e.g. 900']"));
        Assert.DoesNotContain("Monthly Rental", cut.Markup);
        Assert.DoesNotContain("Whole Year", cut.Markup);

        // The daily fee is still asked for, since that is the whole basis.
        Assert.NotEmpty(cut.FindAll("input.avm-input[placeholder='e.g. 30']"));
    }

    [Fact]
    public void OnTheMonthlyGoalTheFormStillAsksForTheMonthlyAmount()
    {
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        Assert.NotEmpty(cut.FindAll("input.avm-input[placeholder='e.g. 900']"));
        Assert.Contains("Monthly Rental", cut.Markup);
    }

    [Fact]
    public void AFormWithNoBasisGivenKeepsTheMonthlyAmount()
    {
        // Every caller that passes no basis, and every facility other than the market, must behave exactly as before.
        var monthlyFacility = new AddVendorModal.VendorModalForm { FacilityCode = "TCC", StallNo = "101" };
        var cut = RenderForm(monthlyFacility);

        Assert.NotEmpty(cut.FindAll("input.avm-input[placeholder='e.g. 900']"));
    }

    /// <summary>A stall being recorded in an area of the office's own, which is the only place a daily fee is typed.</summary>
    private static AddVendorModal.VendorModalForm CustomAreaStall(decimal dailyOnOpen) => new()
    {
        FacilityCode = "NPM",
        StallNo = "12",
        SelectedSection = "__custom__",
        CustomSectionName = "Sari-sari Area",
        CustomDailyRate = dailyOnOpen,
        FeeTypes = new List<string> { "Electricity", "Water" },
    };

    private static void TypeMonthlyRent(IRenderedComponent<AddVendorModal> cut, string rent) =>
        cut.FindAll("input.avm-input").First(i => i.GetAttribute("placeholder") == "e.g. 900").Input(rent);

    private static decimal DailyShown(IRenderedComponent<AddVendorModal> cut) =>
        cut.Instance.Form.CustomDailyRate;

    /// <summary>A stall in one of the three areas the platform starts with, which the ordinance prices.</summary>
    private static AddVendorModal.VendorModalForm CanonicalAreaStall() => new()
    {
        FacilityCode = "NPM",
        StallNo = "7",
        SelectedSection = "Vegetables",
        CustomDailyRate = 0m,
        FeeTypes = new List<string> { "Electricity", "Water" },
    };

    [Fact]
    public void ACantilanAreaStallKeepsTheOrdinanceRateWhateverRentIsTyped()
    {
        // The office's own rule, which this must never contradict: ₱900 a month is ₱30 a day in the ordinance, and a stall
        // in one of the three canonical areas is billed that ₱30 no matter what a clerk types as its contract rent.
        //
        // Guarded twice over, and both are asserted here. The form offers no daily fee field for a canonical area at all,
        // so there is nothing for the assist to write into. And Stall.ResolveDailyFee reads a stall's OWN daily rate only
        // when it is in a custom section, so even a figure that reached the field could not change what this stall is
        // billed.
        var cut = RenderForm(CanonicalAreaStall());

        Assert.Empty(cut.FindAll("input.avm-input[placeholder='e.g. 30']"));

        TypeMonthlyRent(cut, "900");
        Assert.Equal(0m, DailyShown(cut));

        TypeMonthlyRent(cut, "1234");
        Assert.Equal(0m, DailyShown(cut));
        Assert.DoesNotContain("to the nearest peso", cut.Markup);
    }

    [Fact]
    public void WhereTheOfficesOwnArithmeticIsExactTheDerivedFeeAgreesWithIt()
    {
        // ₱900 and ₱30 is Cantilan's own schedule. Rounding cannot disturb a figure that already divides exactly, so an
        // office whose sections follow the market's rule sees its own numbers back.
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        TypeMonthlyRent(cut, "900");

        Assert.Equal(30m, DailyShown(cut));
    }

    [Fact]
    public void NineHundredAMonthFillsThirtyADay()
    {
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        TypeMonthlyRent(cut, "900");

        Assert.Equal(30m, DailyShown(cut));
        Assert.Contains("From ₱900 a month", cut.Markup);
    }

    [Fact]
    public void ARentOfTwelveHundredFillsFortyADay()
    {
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        TypeMonthlyRent(cut, "1200");

        Assert.Equal(40m, DailyShown(cut));
    }

    [Fact]
    public void ASectionThatCarriesItsOwnFeeIsNotOverriddenByTheRent()
    {
        // The field opened blank because the office priced this section. Typing a rent must not put a figure here: it would
        // become the stall's own rate and outrank the section's fee for as long as the stall exists.
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 0m));

        TypeMonthlyRent(cut, "900");

        Assert.Equal(0m, DailyShown(cut));
        Assert.DoesNotContain("From ₱900 a month", cut.Markup);
    }

    [Fact]
    public void AnExistingStallsRateDoesNotMoveBecauseARentWasCorrected()
    {
        // Editing. The stall is being billed this figure already, and a contract correction is not a re-pricing.
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m), isEditing: true);

        TypeMonthlyRent(cut, "1200");

        Assert.Equal(30m, DailyShown(cut));
    }

    [Fact]
    public void AFigureTheClerkTypedThemselvesStands()
    {
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        // The clerk prices this stall at 45 by hand, then goes back and states the rent.
        cut.FindAll("input.avm-input").First(i => i.GetAttribute("placeholder") == "e.g. 30").Input("45");
        TypeMonthlyRent(cut, "900");

        Assert.Equal(45m, DailyShown(cut));
    }

    [Fact]
    public void CorrectingTheRentCorrectsTheDailyFeeItPutThere()
    {
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        TypeMonthlyRent(cut, "900");
        TypeMonthlyRent(cut, "1500");

        Assert.Equal(50m, DailyShown(cut));
    }

    [Fact]
    public void ClearingTheRentLeavesTheDailyFeeAloneRatherThanZeroingIt()
    {
        var cut = RenderForm(CustomAreaStall(dailyOnOpen: 30m));

        TypeMonthlyRent(cut, "900");
        TypeMonthlyRent(cut, "0");

        Assert.Equal(30m, DailyShown(cut));
    }
}
