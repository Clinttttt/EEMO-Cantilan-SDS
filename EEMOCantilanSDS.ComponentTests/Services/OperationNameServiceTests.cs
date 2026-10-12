using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.ComponentTests.Pages;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>
/// What a municipality calls an operation and whether it lists it: presentation only, tenant-scoped by the API, and never a change to
/// a screen of a municipality that has not adopted configuration.
/// </summary>
public sealed class OperationNameServiceTests
{
    private static OperationNameService Service(Mock<IOperationSettingsApiClient> api) => new(api.Object);

    private static Mock<IOperationSettingsApiClient> Api(bool adopted, string[]? listed = null, Dictionary<string, string>? labels = null) =>
        OperationsTenantConfigurationTests.SettingsApi(adopted, listed ?? [], labels);

    [Fact]
    public async Task AnOfficeThatNeverAdopted_KeepsEveryScreensEstablishedWording()
    {
        var names = Service(Api(adopted: false));
        await names.EnsureLoadedAsync();

        Assert.False(names.Adopted);
        Assert.Equal("Kanmanggay", names.NameFor("KANMANGGAY_SPACE_RENTAL", "Kanmanggay"));
        Assert.Equal("Fiesta | Araw", names.NameForSource("FIESTA_ARAW_LOT_RENTAL", null, "Fiesta | Araw"));
        Assert.True(names.IsListed("LANDING_BERTHING"));        // missing configuration is never "everything hidden"
    }

    [Fact]
    public async Task BeforeItHasLoaded_OrAfterAFailure_ItChangesNothing()
    {
        var failing = new Mock<IOperationSettingsApiClient>();
        failing.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Failure("down", 503));
        var names = Service(failing);
        Assert.Equal("Kanmanggay", names.NameFor("KANMANGGAY_SPACE_RENTAL", "Kanmanggay"));
        await names.EnsureLoadedAsync();
        Assert.False(names.Loaded);
        Assert.Equal("Kanmanggay", names.NameFor("KANMANGGAY_SPACE_RENTAL", "Kanmanggay"));
        Assert.True(names.IsListed("LANDING_BERTHING"));

        // A later successful read is picked up (the failure is not remembered).
        failing.Setup(c => c.GetAsync()).ReturnsAsync(Api(true, ["MARKET_FEES"]).Object.GetAsync().Result);
        await names.EnsureLoadedAsync();
        Assert.True(names.Loaded);
    }

    [Fact]
    public async Task AnAdoptedOffice_UsesItsOwnName_ThenTheStandardName_NeverCantilans()
    {
        var names = Service(Api(true, ["KANMANGGAY_SPACE_RENTAL", "FIESTA_ARAW_LOT_RENTAL"],
            new() { ["KANMANGGAY_SPACE_RENTAL"] = "Municipal Plaza Rental" }));
        await names.EnsureLoadedAsync();

        Assert.Equal("Municipal Plaza Rental", names.NameFor("KANMANGGAY_SPACE_RENTAL", "Kanmanggay"));
        Assert.Equal("Event / Temporary Lot Rental", names.NameFor("FIESTA_ARAW_LOT_RENTAL", "Fiesta | Araw")); // not "Fiesta / Araw"
        Assert.Equal("Municipal Plaza Rental", names.NameForSource("KANMANGGAY_SPACE_RENTAL"));
    }

    [Fact]
    public async Task ASourceKey_MapsThroughItsClassification_SoTransportationParkingFindsTheTransportationOperation()
    {
        var names = Service(Api(true, ["TRANSPORTATION"], new() { ["TRANSPORTATION"] = "Parking and Vehicles" }));
        await names.EnsureLoadedAsync();

        Assert.Equal("Parking and Vehicles", names.NameForSource("TRANSPORTATION_PARKING"));
        Assert.Equal("Parking and Vehicles", names.NameForSource("TRANSPORTATION"));
    }

    [Fact]
    public async Task ASourceTheCatalogDoesNotCover_KeepsItsOwnName_EvenForAnAdoptedOffice()
    {
        var names = Service(Api(true, ["MARKET_FEES"]));
        await names.EnsureLoadedAsync();

        Assert.Equal("Fines & Penalties", names.NameForSource("PENALTIES_AND_FINES", null, "Fines & Penalties"));
        Assert.Equal("Tabo", names.NameForSource("TABO", null, "Tabo"));
        Assert.True(names.IsListed("PENALTIES_AND_FINES")); // facility-backed and receivable lines are never hidden by this
    }

    [Fact]
    public async Task AHeadSetPolicyName_StillWins_BecauseItIsTheMoreSpecificEffectiveDatedName()
    {
        var names = Service(Api(true, ["KANMANGGAY_SPACE_RENTAL"], new() { ["KANMANGGAY_SPACE_RENTAL"] = "Municipal Plaza Rental" }));
        await names.EnsureLoadedAsync();

        Assert.Equal("Space Rental (Plaza)", names.NameForSource("KANMANGGAY_SPACE_RENTAL", "Space Rental (Plaza)"));
        // A policy name that is only the code does not count as a name.
        Assert.Equal("Municipal Plaza Rental", names.NameForSource("KANMANGGAY_SPACE_RENTAL", "KANMANGGAY_SPACE_RENTAL"));
    }

    [Fact]
    public async Task AnUnlistedOperation_IsHiddenFromOrdinaryLists_ButNeverWhenItHasRecordedActivity()
    {
        var names = Service(Api(true, ["MARKET_FEES"]));
        await names.EnsureLoadedAsync();

        Assert.True(names.IsListed("MARKET_FEES"));
        Assert.False(names.IsListed("LANDING_BERTHING"));
        Assert.True(names.IsListed("LANDING_BERTHING", hasActivity: true)); // hiding never hides money
    }

    [Fact]
    public async Task ManyCallersShareOneRead_AndInvalidationReloadsAndTellsTheScreens()
    {
        var api = Api(true, ["MARKET_FEES"]);
        var names = Service(api);
        var raised = 0;
        names.Changed += () => raised++;

        await Task.WhenAll(names.EnsureLoadedAsync(), names.EnsureLoadedAsync(), names.EnsureLoadedAsync());
        api.Verify(c => c.GetAsync(), Times.Once);

        await names.InvalidateAsync();
        api.Verify(c => c.GetAsync(), Times.Exactly(2));
        Assert.True(raised >= 2);
    }

    [Fact]
    public void TheCatalogMapsEveryClassificationBackToExactlyOneOperation()
    {
        foreach (var e in TenantOperationCatalog.All)
        {
            Assert.Equal(e.Code, TenantOperationCatalog.FindBySourceKey(e.Code)!.Code);
            Assert.Equal(e.Code, TenantOperationCatalog.FindBySourceKey(e.ClassificationCode)!.Code);
        }
        Assert.Null(TenantOperationCatalog.FindBySourceKey("PERMANENT_STALL_RENT"));
        Assert.Null(TenantOperationCatalog.FindBySourceKey(null));
    }
}
