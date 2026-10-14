using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using ContractsPage = EEMOCantilanSDS.Client.Components.Pages.Reports.ContractsMonitoring;

// Fictional fixtures only: the page is presentation over the monitoring source, so these tests stand in for it.
public sealed class ContractsMonitoringTests : TestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static ContractsMonitoringRow Row(string id, string occupant, string status, DateOnly? expiry, int? days,
        string? duration = "3 years", string? href = "/source/one", string space = "Space 01") =>
        new(id, occupant, "Sample Operation", space, "Signed lease", duration, expiry, status, days,
            new ContractsMonitoringFact[] { new("Name on contract", occupant + " (contract)"), new("Contract reference", "REF-001") }, href);

    private static ContractsMonitoringPage SamplePage(IReadOnlyList<ContractsMonitoringRow>? rows = null, int total = 3, int page = 1, int size = 25) =>
        new(new ContractsMonitoringSummary(12, 1, 3, 2, 4, 1, 0, 2, 21),
            rows ?? new[]
            {
                Row("a", "Alpha Holder", "Active", new DateOnly(2027, 5, 1), 200),
                Row("b", "Beta Holder", "Expiring soon", new DateOnly(2026, 10, 30), 16),
                Row("c", "Gamma Holder", "No contract", null, null, duration: null, href: null),
            },
            total, page, size,
            new[] { new ContractsMonitoringOption("NCC", "Sample Operation") });

    private (IRenderedComponent<ContractsPage> Cut, Mock<IContractsMonitoringSource> Source) Render(Func<ContractsMonitoringQuery, ContractsMonitoringResult>? answer = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var source = new Mock<IContractsMonitoringSource>();
        source.Setup(s => s.LoadAsync(It.IsAny<ContractsMonitoringQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractsMonitoringQuery q, CancellationToken _) => (answer ?? (_ => ContractsMonitoringResult.Ok(SamplePage())))(q));
        Services.AddSingleton(source.Object);

        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalities.Object));
        var facilities = new Mock<IFacilitiesApiClient>();
        Services.AddSingleton(new FacilityState(facilities.Object));
        // Injected for every page by _Imports.razor; this page calls none of them.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        this.AddTestAuthorization().SetAuthorized("SuperAdmin");
        return (RenderComponent<ContractsPage>(), source);
    }

    [Fact]
    public void ShowsTheTitleSubtitleSummaryAndEightColumns()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Contracts Monitoring", cut.Find(".v3-hero, h1, .topbar-title").TextContent);
            Assert.Contains("Track contracts and space occupancy across operations.", cut.Markup);
            foreach (var label in new[] { "Active", "Expiring soon", "Expired", "No contract", "Total monitored" })
                Assert.Contains(label, cut.Markup);
            Assert.Contains("21", cut.Markup);
            Assert.Equal(8, cut.FindAll("thead th").Count);
            Assert.Equal(3, cut.FindAll("tbody tr").Count);
        }, Wait);
    }

    [Fact]
    public void UnknownExpiryReadsNotRecordedAndStatusIsTheServers()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() =>
        {
            var gamma = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Gamma Holder"));
            Assert.Contains("Not recorded", gamma.TextContent);
            Assert.Contains("No contract", gamma.QuerySelector(".cm-status")!.TextContent);
        }, Wait);
    }

    [Fact]
    public void TabsLinkBothRegistersAndMarkContractsCurrent()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll(".mt-tab");
            Assert.Equal(new[] { "/monitoring/follow-up", "/monitoring/contracts" }, tabs.Select(t => t.GetAttribute("href")));
            Assert.Equal("page", tabs[1].GetAttribute("aria-current"));
            Assert.Null(tabs[0].GetAttribute("aria-current"));
        }, Wait);
    }

    [Fact]
    public void ExpiryWindowChoiceAndSearchAreSentToTheServerFromPageOne()
    {
        var (cut, source) = Render();
        cut.WaitForAssertion(() => cut.Find("tbody"), Wait);

        cut.FindAll(".cm-chip").Single(c => c.TextContent == "30 days").Click();
        cut.Find(".cm-search input").Change("Alpha");

        cut.WaitForAssertion(() => source.Verify(s => s.LoadAsync(
            It.Is<ContractsMonitoringQuery>(q => q.ExpiryWindow == 30 && q.Search == "Alpha" && q.Page == 1), It.IsAny<CancellationToken>()), Times.AtLeastOnce), Wait);
    }

    [Fact]
    public void ExpiredChipSendsTheExpiredStatusAndTheApiHasNoTodayWindow()
    {
        var (cut, source) = Render();
        cut.WaitForAssertion(() => cut.Find("tbody"), Wait);
        Assert.DoesNotContain(cut.FindAll(".cm-chip"), c => c.TextContent == "Today");

        cut.FindAll(".cm-chip").Single(c => c.TextContent == "Expired").Click();

        cut.WaitForAssertion(() => source.Verify(s => s.LoadAsync(
            It.Is<ContractsMonitoringQuery>(q => q.Status == "Expired" && q.ExpiryWindow == null), It.IsAny<CancellationToken>()), Times.Once), Wait);
    }

    [Fact]
    public void NextPageAsksTheServerForPageTwoAndPagingIsDisabledAtTheEnds()
    {
        var (cut, source) = Render(q => ContractsMonitoringResult.Ok(SamplePage(total: 60, page: q.Page, size: 25)));
        cut.WaitForAssertion(() => Assert.Contains("1–25 of 60", cut.Find(".cm-pager").TextContent), Wait);
        Assert.True(cut.FindAll(".cm-pager button").First(b => b.TextContent == "Previous").HasAttribute("disabled"));

        cut.FindAll(".cm-pager button").First(b => b.TextContent == "Next").Click();

        cut.WaitForAssertion(() => source.Verify(s => s.LoadAsync(It.Is<ContractsMonitoringQuery>(q => q.Page == 2), It.IsAny<CancellationToken>()), Times.Once), Wait);
    }

    [Fact]
    public void EmptyResultSaysNoContractsMatch()
    {
        var (cut, _) = Render(_ => ContractsMonitoringResult.Ok(SamplePage(Array.Empty<ContractsMonitoringRow>(), total: 0)));
        cut.WaitForAssertion(() => Assert.Contains("No contracts match these filters.", cut.Find(".cm-empty").TextContent), Wait);
    }

    [Fact]
    public void FailureShowsRetryAndRetryLoadsAgain()
    {
        var calls = 0;
        var (cut, _) = Render(_ => ++calls == 1
            ? ContractsMonitoringResult.Fail(ContractsMonitoringFailure.Error)
            : ContractsMonitoringResult.Ok(SamplePage()));
        cut.WaitForAssertion(() => Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent), Wait);

        cut.FindAll("[role=alert] button").Single(b => b.TextContent == "Retry").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count), Wait);
    }

    [Fact]
    public void UnauthorizedAndUnavailableHaveNoRetryAndNoRows()
    {
        var (cut, _) = Render(_ => ContractsMonitoringResult.Fail(ContractsMonitoringFailure.Unavailable));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("not available", cut.Find("[role=alert]").TextContent);
            Assert.Empty(cut.FindAll("[role=alert] button"));
            Assert.Empty(cut.FindAll("tbody tr"));
        }, Wait);
    }

    [Fact]
    public void ViewDetailsListsServerFactsAndLinksToTheSourceRecord()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() => cut.Find("tbody"), Wait);

        cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Alpha Holder")).QuerySelector("button")!.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Contract reference", cut.Find(".cm-facts").TextContent);
            Assert.Contains("200 days remaining", cut.Markup);
            Assert.Equal("/source/one", cut.Find("aside a.v3-btn-primary").GetAttribute("href"));
        }, Wait);
    }

    [Fact]
    public void NoSourceRouteMeansNoLinkAndAnHonestNote()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() => cut.Find("tbody"), Wait);

        cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Gamma Holder")).QuerySelector("button")!.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("aside a.v3-btn-primary"));
            Assert.Contains("Source record unavailable.", cut.Find("aside").TextContent);
        }, Wait);
    }

    [Fact]
    public void TableScrollsInsideItsOwnRegionAndKeepsAnAccessibleName()
    {
        var (cut, _) = Render();
        cut.WaitForAssertion(() =>
        {
            var wrap = cut.Find(".cm-table-wrap");
            Assert.Equal("region", wrap.GetAttribute("role"));
            Assert.Equal("0", wrap.GetAttribute("tabindex"));
            Assert.False(string.IsNullOrWhiteSpace(wrap.GetAttribute("aria-label")));
        }, Wait);
    }
}
