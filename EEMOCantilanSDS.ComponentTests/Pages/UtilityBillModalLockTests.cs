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

        Assert.Single(cut.FindAll(".ub-status-row-locked"));
        Assert.Contains("Recorded as unpaid", cut.Markup);

        // The mis-tap: pressing "Paid" while locked changes nothing and asks instead.
        cut.FindAll(".ub-status")[1].Click();

        Assert.Contains("on", cut.FindAll(".ub-status")[0].GetAttribute("class"));   // still Unpaid
        Assert.DoesNotContain("on", cut.FindAll(".ub-status")[1].GetAttribute("class"));
        Assert.Contains("change it?", cut.Markup);
    }

    [Fact]
    public void SavedStatus_Opens_OnlyWhenTheClerkChoosesToUpdate()
    {
        var cut = RenderModal(Seed(exists: true, waterStatus: "Unpaid"), elec: false);

        cut.Find(".ub-lockbar-btn").Click();

        Assert.Empty(cut.FindAll(".ub-status-row-locked"));
        cut.FindAll(".ub-status")[1].Click();
        Assert.Contains("on", cut.FindAll(".ub-status")[1].GetAttribute("class"));   // Paid now takes
    }

    [Fact]
    public void AFreshBill_HasNothingToProtect_SoTheStatusIsDirectlyEnterable()
    {
        var cut = RenderModal(Seed(exists: false), elec: false);

        Assert.Empty(cut.FindAll(".ub-status-row-locked"));
        Assert.Empty(cut.FindAll(".ub-lockbar"));

        cut.FindAll(".ub-status")[1].Click();

        Assert.Contains("on", cut.FindAll(".ub-status")[1].GetAttribute("class"));
    }
}
