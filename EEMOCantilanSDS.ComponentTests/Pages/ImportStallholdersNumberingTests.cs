using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ImportStallholders = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.ImportStallholders;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The stallholder import's numbering, exercised through the screen rather than through the rule alone.
///
/// <para>The distinction this screen has to hold is easy to state and easy to get wrong in either direction. A number
/// the office wrote on its list is a place — stall 103 is a physical stall — so a deletion must not slide 104 down
/// onto it. A number this screen handed out is its own, so leaving a hole in those reads as a fault. The screen was
/// shipped at both extremes before it was right: first renumbering the whole batch, then renumbering nothing.</para>
/// </summary>
public class ImportStallholdersNumberingTests : TestContext
{
    /// <summary>An empty facility, so nothing in it competes for a number and the numbering starts at 1.</summary>
    private IRenderedComponent<ImportStallholders> Render(string facility, StallHoldersListDto? holders = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallHoldersListAsync(It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>()))
              .ReturnsAsync(Result<StallHoldersListDto>.Success(holders ?? new StallHoldersListDto()));

        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<ImportStallholders>(p => p.Add(c => c.Facility, facility));
    }

    /// <summary>The Stall / Space No. of every review row, in the order the office reads them.</summary>
    private static List<string> Numbers(IRenderedComponent<ImportStallholders> cut) =>
        cut.FindAll("input.imp-stallno").Select(i => i.GetAttribute("value") ?? string.Empty).ToList();

    [Fact]
    public void DeletingASampleRowClosesUpTheNumbers()
    {
        var cut = Render("tcc");
        cut.Find("button.imp-use-sample").Click();

        var before = Numbers(cut);
        Assert.Equal("1", before[0]);
        Assert.Equal("2", before[1]);

        // The row the office would delete: the first one.
        cut.FindAll("button.imp-row-del")[0].Click();

        // The sample's numbers are this screen's own, so they close up. Left as 2, 3, … the screen reads as broken -
        // which is exactly how it was reported.
        var after = Numbers(cut);
        Assert.Equal("1", after[0]);
        Assert.Equal("2", after[1]);
        Assert.Equal(before.Count - 1, after.Count);
    }

    [Fact]
    public void AddingThenDeletingARowLeavesNoGap()
    {
        var cut = Render("tcc");
        cut.Find("button.imp-enter-manually").Click();

        cut.Find("button.imp-add-row").Click();
        cut.Find("button.imp-add-row").Click();
        Assert.Equal(new[] { "1", "2", "3" }, Numbers(cut));

        // The middle one goes.
        cut.FindAll("button.imp-row-del")[1].Click();

        Assert.Equal(new[] { "1", "2" }, Numbers(cut));
    }

    [Fact]
    public void ANumberTheOfficeSuppliedIsNeverMovedByADeletion()
    {
        // Three numbers as though read off the office's list, with a gap it chose: 101, 103, 106. Deleting 103 must
        // leave 101 and 106 exactly as they are. Sliding 106 down to 103 would hand that lessee a different physical
        // stall - the fault that made this screen overwrite the numbers a facility's collections are keyed on.
        var cut = Render("tcc");
        cut.Find("button.imp-enter-manually").Click();
        cut.Find("button.imp-add-row").Click();
        cut.Find("button.imp-add-row").Click();

        cut.FindAll("input.imp-stallno")[0].Input("101");
        cut.FindAll("input.imp-stallno")[1].Input("103");
        cut.FindAll("input.imp-stallno")[2].Input("106");

        Assert.Equal(new[] { "101", "103", "106" }, Numbers(cut));

        cut.FindAll("button.imp-row-del")[1].Click();

        Assert.Equal(new[] { "101", "106" }, Numbers(cut));
    }
}
