using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Income From Terminal is its own operation: three office sections on a Cash Ticket, direct amounts, and the vehicle classes by section.</summary>
public sealed class TerminalWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficeSourcesApiClient> _office = new();
    private readonly Mock<IVehicleClassesApiClient> _classes = new();

    public TerminalWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(_office.Object);
        Services.AddSingleton(_classes.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private static SourceNativeActivityDto Row(TerminalSection section, string src, decimal amount, int? tickets) =>
        new(Guid.NewGuid(), src, PhilippineTime.Today, DateTime.UtcNow, CollectorOperationCodes.Terminal, section, null, null, null, "Cora", amount, amount, "Posted", tickets, null, null, null, null);

    [Fact]
    public void TheThreeOfficeSectionsAreShownByTheirOfficeNames_WithTheirOwnTotals()
    {
        _office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), CollectorOperationCodes.Terminal)).ReturnsAsync(
            Result<IReadOnlyList<SourceNativeActivityDto>>.Success([
                Row(TerminalSection.ComfortRoom, "SRC-2026-000021", 400m, 40),
                Row(TerminalSection.PullPulVansCargoVans, "SRC-2026-000022", 5800m, null),
                Row(TerminalSection.Tricycad, "SRC-2026-000023", 900m, 30)]));
        _office.Setup(x => x.VehicleChoicesAsync(It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<TerminalVehicleChoice>>.Success([]));
        _classes.Setup(x => x.GetAsync()).ReturnsAsync(Result<IReadOnlyList<VehicleClassDto>>.Success([]));

        var cut = RenderComponent<Terminal>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Income From Terminal", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            var sections = cut.FindAll("section[aria-labelledby='trm2-sections-title'] tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(sections, r => r.Contains("COMFORT ROOM") && r.Contains("₱400.00") && r.Contains("40"));
            Assert.Contains(sections, r => r.Contains("PULL PUL VANS, CARGO VANS") && r.Contains("₱5,800.00"));
            Assert.Contains(sections, r => r.Contains("TRICYCAD") && r.Contains("₱900.00") && r.Contains("30"));
            Assert.Contains("SRC-2026-000022", cut.Markup);
            Assert.DoesNotContain("Transportation", cut.Markup);
            Assert.DoesNotContain("TRM", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void KnownVehicleCodesShowTheirConfirmedSection_AndOnlyACustomClassNeedsTheHeadsMapping()
    {
        VehicleClassDto Cls(string code, string name) => new(Guid.NewGuid(), code, name, true, 20m, PhilippineTime.Today);
        var jeepney = Cls("JEEPNEY", "Jeepney"); var tricycle = Cls("TRICYCLE", "Tricycle"); var pedicab = Cls("PEDICAB", "Pedicab");
        _classes.Setup(x => x.GetAsync()).ReturnsAsync(Result<IReadOnlyList<VehicleClassDto>>.Success([jeepney, tricycle, pedicab]));
        _office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]));
        _office.Setup(x => x.VehicleChoicesAsync(It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<TerminalVehicleChoice>>.Success([]));
        TerminalVehicleMappingRequest? mapped = null;
        _office.Setup(x => x.MapVehicleAsync(It.IsAny<TerminalVehicleMappingRequest>())).Callback<TerminalVehicleMappingRequest>(r => mapped = r).ReturnsAsync(Result<bool>.Success(true));

        var cut = RenderComponent<Terminal>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("section[aria-labelledby='trm2-classes-title'] tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Jeepney") && r.Contains("PULL PUL VANS, CARGO VANS"));      // exact code: no re-mapping needed
            Assert.Contains(rows, r => r.Contains("Tricycle") && r.Contains("TRICYCAD"));
            Assert.Contains(rows, r => r.Contains("Pedicab") && r.Contains("Not mapped"));
            Assert.Single(cut.FindAll("select.trm2-map"));                                                       // only the custom class offers a mapping
        }, Timeout);

        cut.Find("select.trm2-map").Change(((int)TerminalSection.Tricycad).ToString());
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(mapped);
            Assert.Equal((pedicab.Id, TerminalSection.Tricycad), (mapped!.VehicleClassId, mapped.Section));
        }, Timeout);
    }

    [Fact]
    public void TheOldTerminalAddressForwardsToIncomeFromTerminal()
    {
        RenderComponent<TRM>();
        Assert.EndsWith("/operations/terminal", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
