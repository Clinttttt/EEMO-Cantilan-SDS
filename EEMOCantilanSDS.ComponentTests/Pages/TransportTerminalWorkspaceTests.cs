using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Transport Terminal is read from the canonical Transportation / Parking collections: today's SRC-bearing collections, the server's
/// totals, and nothing that asks the Head to maintain transporters. The earlier terminal trips stay behind "Legacy trip history".
/// </summary>
public sealed class TransportTerminalWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IGovernedServicesApiClient> _governed = new();
    private readonly Mock<IVehicleClassesApiClient> _classes = new();

    public TransportTerminalWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_governed.Object);
        Services.AddSingleton(_classes.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _classes.Setup(x => x.GetAsync()).ReturnsAsync(Result<IReadOnlyList<VehicleClassDto>>.Success([
            new(Guid.NewGuid(), "JN", "Jeepney", true, 20m, new DateOnly(2026, 10, 1)),
            new(Guid.NewGuid(), "VAN", "Van", true, null, null)]));
    }

    private static GovernedServiceActivityDto Row(decimal amount, string src) => new(
        Guid.NewGuid(), PhilippineTime.Today, DateTime.UtcNow, src, RevenueInstrumentType.CashTicket, null, "Jeepney 12", "Terminal gate", amount, "Ana Reyes", "Posted");

    private void Serve(params GovernedServiceActivityDto[] rows) =>
        _governed.Setup(x => x.GetActivityAsync(CollectorOperationCodes.Transportation, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success(rows));

    [Fact]
    public void Routes_TheCurrentPageIsTheCanonicalOne_AndTheOldTripsLiveBehindLegacyHistory()
    {
        var routes = typeof(TRM).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Select(x => x.Template).ToArray();
        Assert.Contains("/facility/trm", routes);
        Assert.Equal("/facility/trm/legacy", Assert.Single(typeof(TrmLegacyTrips).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
    }

    [Fact]
    public void Shows_TodaysCanonicalCollections_WithTheirSrc_AndTheServersTotals_AndNoTransporterRegister()
    {
        Serve(Row(20m, "SRC-2026-000010"), Row(30m, "SRC-2026-000011"));

        var cut = RenderComponent<TRM>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("tbody tr").Count);
            Assert.Contains("SRC-2026-000010", cut.Markup);
            Assert.Contains("₱50.00", cut.Find("tfoot").TextContent);
            var figures = cut.Find("dl.v3h-figures").TextContent;
            Assert.Contains("Collected today", figures);
            Assert.Contains("₱50.00", figures);
            Assert.Contains("Transactions today", figures);
            Assert.Contains("1", cut.FindAll(".v3h-figure").Single(f => f.TextContent.Contains("Vehicle classes")).TextContent);    // only the class with a rate in force
            Assert.DoesNotContain("Registered Transporters", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Add Transporter"));
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/facility/trm/legacy" && a.TextContent.Contains("Legacy trip history"));
            Assert.NotNull(cut.Find(".trm-scroll thead th"));                                                                        // a bounded, sticky-headed register
        }, Timeout);
    }

    [Fact]
    public void EmptyAndFailedStates_AreIntentional_AndTheFailureCanBeRetried()
    {
        _governed.Setup(x => x.GetActivityAsync(CollectorOperationCodes.Transportation, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Failure("offline"));

        var cut = RenderComponent<TRM>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Collections are unavailable.", cut.Markup);
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Try again");
        }, Timeout);

        Serve();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Try again").Click();
        cut.WaitForAssertion(() => Assert.Contains("No transportation collections were recorded today.", cut.Markup), Timeout);
    }
}
