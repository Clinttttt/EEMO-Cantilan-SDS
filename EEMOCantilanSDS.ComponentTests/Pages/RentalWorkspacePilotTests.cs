using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class RentalWorkspacePilotTests : TestContext
{
    private readonly Mock<IObligationsApiClient> _obligations = new();
    private readonly TestAuthorizationContext _authorization;
    public RentalWorkspacePilotTests()
    {
        Services.AddSingleton(_obligations.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([
                new(FacilityCode.ICE, "Tenant Cold Storage", "ICE", 0, null, null, null)]));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton<FacilityState>();
        Services.AddSingleton<BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _authorization = this.AddTestAuthorization();
        _authorization.SetAuthorized("head").SetRoles("SuperAdmin");
    }

    [Theory]
    [InlineData(RentalWorkspaceKey.Ice, "ICE", "/facility/ice", "/import/ice", "Space")]
    [InlineData(RentalWorkspaceKey.Kanmanggay, "KM", "/operations/kanmanggay", "/operations/kanmanggay/import", "Space")]
    [InlineData(RentalWorkspaceKey.FiestaAraw, "FA", "/operations/fiesta-araw", "/operations/fiesta-araw/import", "Lot")]
    public void CatalogKeepsPresentationRoutesAndSourcePoliciesDistinct(RentalWorkspaceKey key, string code, string overview, string import, string unit)
    {
        var workspace = RentalWorkspaceCatalog.Get(key);
        Assert.Equal(code, workspace.DisplayCode);
        Assert.Equal(overview, workspace.OverviewRoute);
        Assert.Equal(import, workspace.ImportRoute);
        Assert.Equal(unit, workspace.Unit(Services.GetRequiredService<FacilityState>()));
        Assert.True(workspace.RegistrationAvailable);
        Assert.Equal(key == RentalWorkspaceKey.Ice ? new FacilityRentalSource(FacilityCode.ICE)
            : key == RentalWorkspaceKey.Kanmanggay ? (RentalWorkspaceSource)new MonthlySpaceRentalSource(ObligationKind.KanmanggaySpaceRental)
            : new EventLotRentalSource(ObligationKind.FiestaArawLotRental), workspace.Source);
        if (key == RentalWorkspaceKey.FiestaAraw)
        {
            Assert.False(workspace.Sections.HasFlag(RentalSection.MonthlyRate));
            Assert.False(workspace.Sections.HasFlag(RentalSection.ContractEvidence));
            Assert.Equal("/operations/fiesta-araw/space-holders", workspace.RosterRoute);   // the lot holder list the office reads like the other operations' lists
            Assert.Equal("/vendors?workspace=FA", workspace.ManagementRoute);
            Assert.Equal(RentalContractPolicy.EventOnly, workspace.ContractPolicy);
        }
        if (key == RentalWorkspaceKey.Kanmanggay)
        {
            Assert.False(workspace.Sections.HasFlag(RentalSection.Area));
            Assert.Equal(RentalNumberingPolicy.ServerAssignedSpace, workspace.Numbering);
            Assert.Equal(RentalContractPolicy.ReportedThreeYearStandardPendingLifecycleApproval, workspace.ContractPolicy);
        }
    }

    [Fact]
    public async Task SharedHeroUsesTenantFacilityNameAndKeepsCallerAmountsAndScope()
    {
        await Services.GetRequiredService<FacilityState>().EnsureLoadedAsync();
        var cut = RenderComponent<RentalWorkspaceHero>(p => p.Add(x => x.Workspace, RentalWorkspaceCatalog.Ice)
            .Add(x => x.Scope, "October 2026").Add(x => x.Stats, [new() { Value = "₱1,234.56", Label = "Collected" }]));
        Assert.Equal("Tenant Cold Storage", cut.Find("h1").TextContent);
        Assert.Equal("October 2026", cut.Find(".facility-hero-scope").TextContent);
        Assert.Contains("₱1,234.56", cut.Markup);
    }

    [Fact]
    public void AdoptedMunicipalSpaceRentalName_AlsoAppearsOnWorkspaceHero()
    {
        var api = OperationsTenantConfigurationTests.SettingsApi(
            adopted: true, listed: ["KANMANGGAY_SPACE_RENTAL"]);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new OperationNameService(api.Object));
        _obligations.Setup(x => x.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental))
            .ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([], 0m, 0m, 0m)));

        var cut = RenderComponent<Kanmanggay>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Municipal Space Rental", cut.Find(".facility-hero h1").TextContent.Trim());
            Assert.Contains("Municipal Space Rental", cut.Find(".topbar-title").TextContent);
            Assert.Contains("Municipal Space Rental totals",
                cut.Find("[role='region'][aria-label*='totals']").GetAttribute("aria-label"));
        }, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(RentalWorkspaceKey.Ice, 3)]
    [InlineData(RentalWorkspaceKey.Kanmanggay, 3)]
    [InlineData(RentalWorkspaceKey.FiestaAraw, 3)]
    public void NavigationOnlyOffersExistingRoutesAndNeverInventsAdministrativeActions(RentalWorkspaceKey key, int links)
    {
        var workspace = RentalWorkspaceCatalog.Get(key);
        var cut = RenderComponent<RentalWorkspaceNavigation>(p => p.Add(x => x.Workspace, workspace));
        Assert.Equal(links, cut.FindAll("a").Count);
        Assert.Empty(cut.FindAll("button"));
        Assert.NotNull(cut.Find($"a[href='{workspace.ReportsRoute}']"));
        cut.SetParametersAndRender(p => p.Add(x => x.ShowManagement, false));
        Assert.DoesNotContain(cut.FindAll("a"), x => x.TextContent.StartsWith("Manage"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PilotUsesCorrectSourceBalancesSectionsAndPermissions(bool eventRental)
    {
        var workspace = eventRental ? RentalWorkspaceCatalog.FiestaAraw : RentalWorkspaceCatalog.Kanmanggay;
        var kind = eventRental ? ObligationKind.FiestaArawLotRental : ObligationKind.KanmanggaySpaceRental;
        var account = new ObligationAccountDto(Guid.NewGuid(), kind, workspace.OperationName!, Guid.Empty, "Holder", null, null,
            "007", eventRental ? LotRentalEvent.Araw : null, eventRental ? new(2026, 10, 9) : null,
            new(2026, 10, 1), null, 900m, new(2026, 10, 1), 1800m, 400m, 1400m, ActualOccupant: "Source holder");
        _obligations.Setup(x => x.GetWorkspaceAsync(kind)).ReturnsAsync(Result<ObligationWorkspaceDto>.Success(new([account], 1800m, 400m, 1400m)));
        var cut = Render(builder => { builder.OpenComponent(0, eventRental ? typeof(FiestaAraw) : typeof(Kanmanggay)); builder.CloseComponent(); });
        Assert.Equal(workspace.TableTitle, cut.Find(".rental-summary-title").TextContent);
        Assert.Equal($"OPERATION · {workspace.DisplayCode}", cut.Find(".facility-hero-eyebrow").TextContent);
        Assert.Equal("All accounts · to date", cut.Find(".facility-hero-scope").TextContent);
        Assert.Contains(eventRental ? "Registered lots" : "Registered spaces", cut.Markup);
        Assert.Contains("Source holder", cut.Find("tbody").TextContent);
        if (eventRental)
        {
            Assert.Contains("Araw", cut.Find("tbody").TextContent);
            Assert.NotNull(cut.FindComponent<RegisteredRentalTable<ObligationAccountDto>>());
            Assert.NotNull(cut.Find("td[data-label='Approved event amount']"));
        }
        SpaceHolderWorkspaceTests.SaveVisualArtifact(eventRental ? "fiesta-araw-overview" : "kanmanggay-pilot", cut.Markup);
        var hero = cut.Find(".facility-hero").TextContent;
        cut.Find(".search-input").Input("absent");
        Assert.Equal(hero, cut.Find(".facility-hero").TextContent);
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "+ Add New").Click();
        var titles = cut.FindAll(".avm-section-label").Select(x => x.TextContent).ToArray();
        Assert.Equal(eventRental ? new[] { "Lessee Information", "Rent / Space & Event Details", "Space Information", "Rental Rates", "Additional Information", "Applicable Charges" }
            : new[] { "Lessee Information", "Rent / Space & Contract Details", "Rent / Space Information", "Rental Rates", "Additional Information", "Applicable Charges" }, titles);
        Assert.Single(cut.FindAll("#obw-amount"));
        if (eventRental)
        {
            Assert.Contains("One-time event rental", cut.Find("form").TextContent);
            Assert.Empty(cut.FindAll("input[aria-label='Custom contract duration in years'], #obw-contract-date, #obw-area, #obw-area-note"));
            Assert.NotNull(cut.FindComponent<RentalLesseeFields>());
            Assert.All(cut.FindComponents<RentalRateFields>(), rate => Assert.True(rate.Instance.OneTime));
            Assert.DoesNotContain("Whole Year", cut.Find("form").TextContent);
        }
        else
        {
            Assert.Contains("Contract Duration (No. of Years)", cut.Find("form").TextContent);
            Assert.NotNull(cut.Find("#obw-area"));
            Assert.NotNull(cut.Find("#obw-area-note"));
        }
        Assert.Equal(eventRental, cut.FindAll("#obw-event-date").Any());
        Assert.Equal(!eventRental, cut.FindAll("#obw-contract-date").Any());
        Assert.Equal(eventRental, cut.FindAll("#obw-subject").Any());
        cut.Find(".eemo-drawer-header-close").Click();                                    // the one dismiss control: the header X (an untouched form closes at once)
        _authorization.SetRoles("Admin");
        Assert.DoesNotContain(cut.FindAll("button"), x => x.TextContent is "+ Add New" or "Close account" or "Change amount");
        _obligations.Verify(x => x.GetWorkspaceAsync(kind), Times.Once);
        Assert.All(_obligations.Invocations, x => Assert.Contains(x.Method.Name,
            new[] { nameof(IObligationsApiClient.GetWorkspaceAsync), nameof(IObligationsApiClient.GetSpaceOperationsAsync) }));
    }

    [Fact]
    public void MismatchedSourceAdapterFailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() => RenderComponent<ObligationWorkspace>(p => p
            .Add(x => x.Workspace, RentalWorkspaceCatalog.FiestaAraw).Add(x => x.Kind, ObligationKind.KanmanggaySpaceRental)
            .Add(x => x.Name, "Kanmanggay").Add(x => x.SubjectNoun, "Space")));
    }
}
