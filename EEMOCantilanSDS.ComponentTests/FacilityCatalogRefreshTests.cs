using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Whether a name the office has just entered reaches the screens that show it.
///
/// <para>
/// The facility catalogue is read once per circuit, which is right: it is small, and every screen names facilities
/// from it. It also meant a rename in Facility Configuration reached nothing already on screen. The office typed
/// its market's name, saw the sidebar and the market's own page still carrying the old one, and had to reload the
/// browser to make its own correction appear.
/// </para>
/// </summary>
public class FacilityCatalogRefreshTests
{
    private static Mock<IFacilitiesApiClient> Api(params string[] namesInOrder)
    {
        var api = new Mock<IFacilitiesApiClient>();
        var calls = 0;
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(() =>
           {
               var name = namesInOrder[System.Math.Min(calls, namesInOrder.Length - 1)];
               calls++;
               return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
               {
                   new(FacilityCode.NPM, name, string.Empty, 0),
               });
           });
        return api;
    }

    [Fact]
    public async Task ReadingTheCatalogueTwiceDoesNotRefetchIt()
    {
        var api = Api("Madrid Public Market");
        var state = new FacilityState(api.Object);

        await state.EnsureLoadedAsync();
        await state.EnsureLoadedAsync();

        api.Verify(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task ARenameIsVisibleAfterAReload_WithoutTheCircuitRestarting()
    {
        var api = Api("Madrid Public Market", "Madrid New Public Market");
        var state = new FacilityState(api.Object);

        await state.EnsureLoadedAsync();
        Assert.Equal("Madrid Public Market", state.NameOf(FacilityCode.NPM));

        await state.ReloadAsync();

        Assert.Equal("Madrid New Public Market", state.NameOf(FacilityCode.NPM));
    }

    [Fact]
    public async Task AReloadTellsTheScreen()
    {
        // What the sidebar and any other mounted component listen to, so they need no reload of their own.
        var api = Api("Madrid Public Market", "Madrid New Public Market");
        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();

        var announced = 0;
        state.Changed += () => announced++;

        await state.ReloadAsync();

        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task AnAcronymChangeReachesTheScreensToo()
    {
        // The acronym heads reports and the Export Data chips, and is derived from the name when the office has
        // recorded none - so it has to follow the name it is derived from.
        var api = new Mock<IFacilitiesApiClient>();
        var calls = 0;
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(() =>
           {
               var acronym = calls++ == 0 ? "MPM" : "MNPM";
               return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
               {
                   new(FacilityCode.NPM, "Madrid Public Market", acronym, 0),
               });
           });

        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();
        Assert.Equal("MPM", state.ShortNameOf(FacilityCode.NPM));

        await state.ReloadAsync();

        Assert.Equal("MNPM", state.ShortNameOf(FacilityCode.NPM));
    }

    [Fact]
    public async Task AFailedReloadKeepsTheNamesAlreadyOnScreen()
    {
        // Presentation only: a transient failure must not blank the office's own names.
        var api = new Mock<IFacilitiesApiClient>();
        var calls = 0;
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(() =>
           {
               if (calls++ == 0)
                   return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
                   {
                       new(FacilityCode.NPM, "Madrid Public Market", "MPM", 0),
                   });
               return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("network");
           });

        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();

        await state.ReloadAsync();

        Assert.Equal("Madrid Public Market", state.NameOf(FacilityCode.NPM));
        Assert.Equal("MPM", state.ShortNameOf(FacilityCode.NPM));
    }

    [Fact]
    public async Task NewMarketAreaLabelsReachTheScreensToo()
    {
        var api = new Mock<IFacilitiesApiClient>();
        var calls = 0;
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(() =>
           {
               var veg = calls++ == 0 ? null : "Gulayan";
               return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
               {
                   new(FacilityCode.NPM, "Madrid Public Market", "MPM", 0, veg, null, null),
               });
           });

        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();
        Assert.Equal("Vegetable Area", state.SectionLabelOf(FacilityCode.NPM, MarketSection.VegetableArea));

        await state.ReloadAsync();

        Assert.Equal("Gulayan", state.SectionLabelOf(FacilityCode.NPM, MarketSection.VegetableArea));
    }
}
