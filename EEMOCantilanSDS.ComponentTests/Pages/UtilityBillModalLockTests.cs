using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using UtilityBillModal = EEMOCantilanSDS.Client.Components.Modals.UtilityBillModal;

/// <summary>
/// bUnit render tests for the two locks on the utility entry dialog.
///
/// Both exist because the dialog treated a record as an entry. The PREVIOUS reading is carried forward from the
/// last bill and was being typed over by accident; the payment STATUS of a saved bill is a receipted fact and a
/// mis-tap on "Unpaid" silently re-marked it. Neither is now changed by a stray click: the previous reading is
/// stated until the clerk asks to correct it, and a saved status only opens when they say they mean to.
/// </summary>
public class UtilityBillModalLockTests : TestContext
{
    private static UtilityBillEntryDto Seed(
        bool exists = false,
        decimal elecPrev = 120m, decimal elecCur = 150m,
        decimal waterPrev = 40m, decimal waterCur = 56m,
        string elecStatus = "Unpaid", string waterStatus = "Unpaid") =>
        new(exists,
            ElecPreviousReading: elecPrev, ElecCurrentReading: elecCur, ElecRatePerKwh: 1m,
            WaterPreviousReading: waterPrev, WaterCurrentReading: waterCur, WaterRatePerCubicMeter: 1m,
            ElecStatus: elecStatus, ElecPartialAmount: 0m,
            WaterStatus: waterStatus, WaterPartialAmount: 0m,
            ElecORNumber: null, WaterORNumber: null);

    private IRenderedComponent<UtilityBillModal> RenderModal(UtilityBillEntryDto seed, bool water = true, bool elec = true)
    {
        // A utility the stall is not billed for stays hidden only while this month's bill carries no figures
        // for it, so a seed for a water-only stall must be a water-only seed.
        if (!elec) seed = seed with { ElecPreviousReading = 0m, ElecCurrentReading = 0m };
        if (!water) seed = seed with { WaterPreviousReading = 0m, WaterCurrentReading = 0m };

        var api = new Mock<IUtilitiesApiClient>();
        api.Setup(a => a.GetBillForEntryAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<UtilityBillEntryDto>.Success(seed));
        Services.AddSingleton(api.Object);

        // The global _Imports.razor injects these into every component; stub them so the dialog resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<UtilityBillModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.StallId, Guid.NewGuid())
            .Add(c => c.StallNo, "3")
            .Add(c => c.Occupant, "Dante Revilla")
            .Add(c => c.Year, 2026)
            .Add(c => c.Month, 8)
            .Add(c => c.HasElectricity, elec)
            .Add(c => c.HasWater, water));
    }

    [Fact]
    public void PreviousReading_IsStated_NotAnInput_UntilTheClerkAsks()
    {
        var cut = RenderModal(Seed(), elec: false);   // water only, to keep one meter in the assertions

        // Previous is a stated figure; Current and Rate remain inputs.
        var locked = cut.Find(".ub-locked");
        Assert.Contains("40.00", locked.TextContent);
        Assert.Equal(2, cut.FindAll(".ub-grid3 input").Count);

        locked.Click();

        Assert.Empty(cut.FindAll(".ub-locked"));
        Assert.Equal(3, cut.FindAll(".ub-grid3 input").Count);   // now editable
    }

    [Fact]
    public void ASettledUtility_LocksEveryReading_AndSaysWhy()
    {
        // Water is already receipted. The server locks previous, current AND rate for that utility
        // (WouldChangeSettledReadings compares all three), so the dialog must not invite an edit that would
        // come back as a rejected save.
        var cut = RenderModal(Seed(exists: true, waterStatus: "Paid"), elec: false);

        Assert.Equal(3, cut.FindAll(".ub-locked").Count);          // previous, current and rate
        Assert.Empty(cut.FindAll(".ub-grid3 input"));              // nothing typeable
        Assert.Empty(cut.FindAll(".ub-locked-edit"));              // no pencil offered at all
        Assert.Contains("A payment is recorded for this utility", cut.Markup);
        // Stated as a fact, not as a validation failure.
        Assert.Single(cut.FindAll(".ub-lock-note"));
        Assert.Empty(cut.FindAll(".ub-error"));
    }

    [Fact]
    public void SavedStatus_IsLocked_AndAChipPressDoesNotReMarkIt()
    {
        var cut = RenderModal(Seed(exists: true, waterStatus: "Unpaid"), elec: false);

        // The chip that holds the recorded status carries the lock; the row is out of reach until asked.
        Assert.Single(cut.FindAll(".ub-status-wrap.locked"));
        Assert.Single(cut.FindAll(".ub-status.on .ub-ico"));
        Assert.Single(cut.FindAll(".ub-status-unlock"));

        // The mis-tap: pressing "Paid" while locked changes nothing, and brings the way out into view.
        cut.FindAll(".ub-status")[1].Click();

        Assert.Contains("on", cut.FindAll(".ub-status")[0].GetAttribute("class"));   // still Unpaid
        Assert.DoesNotContain("on", cut.FindAll(".ub-status")[1].GetAttribute("class"));
        Assert.Single(cut.FindAll(".ub-status-wrap.asked"));
    }

    [Fact]
    public void SavedStatus_Opens_OnlyWhenTheClerkChoosesToUpdate()
    {
        var cut = RenderModal(Seed(exists: true, waterStatus: "Unpaid"), elec: false);

        cut.Find(".ub-status-unlock").Click();

        Assert.Empty(cut.FindAll(".ub-status-wrap.locked"));
        Assert.Empty(cut.FindAll(".ub-status-unlock"));
        cut.FindAll(".ub-status")[1].Click();
        Assert.Contains("on", cut.FindAll(".ub-status")[1].GetAttribute("class"));   // Paid now takes
    }

    [Fact]
    public void AFreshBill_HasNothingToProtect_SoTheStatusIsDirectlyEnterable()
    {
        var cut = RenderModal(Seed(exists: false), elec: false);

        Assert.Empty(cut.FindAll(".ub-status-wrap.locked"));
        Assert.Empty(cut.FindAll(".ub-status-unlock"));

        cut.FindAll(".ub-status")[1].Click();

        Assert.Contains("on", cut.FindAll(".ub-status")[1].GetAttribute("class"));
    }

    [Fact]
    public void ANewMonth_DoesNotRepeatLastMonthsReadingAsThisMonths()
    {
        // The reported confusion: the Current box opened holding last month's figure, so the month looked as
        // though it had already been read — and a clerk who overwrote it with the month's CONSUMPTION was
        // refused for a reading that had "gone backwards".
        var cut = RenderModal(Seed(exists: false, waterPrev: 56m, waterCur: 56m), elec: false);

        // Previous is the figure carried forward, stated rather than typed.
        Assert.Contains("56.00", cut.Find(".ub-locked .ub-locked-value").TextContent);

        // Current and Rate are the inputs; the current reading waits to be stated.
        var inputs = cut.FindAll(".ub-grid3 input");
        Assert.True(string.IsNullOrEmpty(inputs[0].GetAttribute("value")));
        Assert.Equal("Meter now", inputs[0].GetAttribute("placeholder"));
        // Nothing is charged until it is read: no usage, nothing due.
        Assert.Contains("0.00", cut.Find(".ub-usage-value").TextContent);
        Assert.Contains("₱0.00", cut.Find(".ub-usage-amount").TextContent);
    }

    [Fact]
    public void AnExistingMonth_StillShowsTheReadingOnRecord()
    {
        var cut = RenderModal(Seed(exists: true, waterPrev: 40m, waterCur: 56m), elec: false);

        Assert.Contains("40.00", cut.Find(".ub-locked .ub-locked-value").TextContent);
        Assert.Equal("56", cut.FindAll(".ub-grid3 input")[0].GetAttribute("value"));
        Assert.Contains("16.00", cut.Find(".ub-usage-value").TextContent);
        Assert.Contains("₱16.00", cut.Find(".ub-usage-amount").TextContent);
    }

    [Fact]
    public void AReadingBelowTheLastOne_IsRefusedWithTheFigureItMustClear()
    {
        var cut = RenderModal(Seed(exists: false, waterPrev: 56m, waterCur: 56m), elec: false);

        cut.FindAll(".ub-grid3 input")[0].Input("44");   // the current reading, bound on input
        cut.Find(".ub-save").Click();

        Assert.Contains("must be at least 56.00", cut.Markup);
        Assert.Contains("not the month's consumption", cut.Markup);
    }

    [Fact]
    public void TheReceiptNumber_IsNotAskedForHere()
    {
        // The receipt is captured where the money is taken. This dialog records readings and a status, so it
        // does not ask for an OR again — re-typing one is how a receipted month acquires a second number.
        var cut = RenderModal(Seed(exists: true, waterStatus: "Paid"), elec: false);

        Assert.DoesNotContain("OR number", cut.Markup);
        Assert.Empty(cut.FindAll("input[type=text]"));
    }
}
