using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using ClosedAccounts = EEMOCantilanSDS.Client.Components.Pages.Reports.ClosedAccounts;

/// <summary>
/// The Closed Accounts register across every source that can hold an inactive account: stall accounts with their full
/// lifecycle, and the source-owned space accounts that are shown read-only because their sources offer no renew, reopen or
/// successor operation.
/// </summary>
public sealed class ClosedAccountsCoverageTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly List<Guid> Renewed = new();
    private readonly HashSet<Guid> _failRenewals = new();
    private readonly Mock<ILifecycleApiClient> _lifecycleApi = new();

    private static ClosedStallAccountDto Stall(InactiveAccountState state, string occupant, string stallNo, decimal uncollected = 0m, bool reLet = false,
        FacilityCode facility = FacilityCode.NPM, string facilityName = "New Public Market") => new(
        StallId: Guid.NewGuid(), State: state, FacilityCode: facility, FacilityName: facilityName, StallNo: stallNo, Occupant: occupant,
        ContractName: occupant, EffectivityDate: new DateOnly(2023, 6, 1), DurationYears: 3, MonthlyRate: 900m,
        ClosedOn: state == InactiveAccountState.Closed ? new DateOnly(2026, 5, 1) : null, ExpiryDate: new DateOnly(2026, 6, 1),
        LifetimeCollected: 12_000m, Uncollected: uncollected, ClosedBy: null, Section: "", OccupancyEndedOn: new DateOnly(2026, 6, 1),
        StallReLet: reLet, ContractId: Guid.NewGuid(), AreaSqm: null, AreaNote: null, MonthBasis: NpmMonthBasis.RentGoal);

    private static ObligationAccountDto Space(ObligationKind kind, string holder, string unit, decimal outstanding = 0m,
        LotRentalEvent? @event = null, DateOnly? eventDate = null) => new(
        Guid.NewGuid(), kind, kind.ToString(), Guid.NewGuid(), holder, null, null, unit, @event, eventDate,
        new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 31), 500m, new DateOnly(2025, 1, 1), 6000m, 6000m - outstanding, outstanding,
        OccupancyArrangement.SpaceOnly, null, holder);

    private IRenderedComponent<ClosedAccounts> RenderRegister(IEnumerable<ClosedStallAccountDto> stalls, params ObligationAccountDto[] spaces)
    {
        var stallsApi = new Mock<IStallsApiClient>();
        stallsApi.Setup(a => a.GetClosedStallAccountsAsync()).ReturnsAsync(Result<IReadOnlyList<ClosedStallAccountDto>>.Success(stalls.ToList()));
        stallsApi.Setup(a => a.RenewStallContractAsync(It.IsAny<Guid>(), It.IsAny<EEMOCantilanSDS.Application.Requests.Stalls.RenewStallContractRequest>()))
            .ReturnsAsync((Guid id, EEMOCantilanSDS.Application.Requests.Stalls.RenewStallContractRequest _) =>
            {
                Renewed.Add(id);
                return _failRenewals.Contains(id) ? Result<bool>.Failure("Refused") : Result<bool>.Success(true);
            });
        var obligations = new Mock<IObligationsApiClient>();
        obligations.Setup(x => x.GetWorkspaceAsync(It.IsAny<ObligationKind>())).ReturnsAsync((ObligationKind kind) =>
            Result<ObligationWorkspaceDto>.Success(new(spaces.Where(s => s.Kind == kind).ToList(), 0m, 0m, 0m)));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(stallsApi.Object);
        Services.AddSingleton(obligations.Object);
        Services.AddSingleton(_lifecycleApi.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        var cut = RenderComponent<ClosedAccounts>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ca-table")), Timeout);
        return cut;
    }

    private static void Choose(IRenderedComponent<ClosedAccounts> cut, string caption, string label)
    {
        cut.FindAll(".fh-dd").Single(d => d.QuerySelector(".fh-dd-cap")?.TextContent.Trim() == caption).QuerySelector(".fh-dd-trigger")!.Click();
        cut.FindAll(".fh-dd-item").Single(i => i.TextContent.Trim() == label).Click();
    }

    private static string Csv(IRenderedComponent<ClosedAccounts> cut)
    {
        var href = cut.Find("a.ca-export-btn").GetAttribute("href")!;
        return Uri.UnescapeDataString(href["data:text/csv;charset=utf-8,".Length..]);
    }

    [Fact]
    public void ToolbarIsCompact_ExportIsOneJoinedControl_AndTheBreadcrumbDropsTheOfficeAcronym()
    {
        var cut = RenderRegister([Stall(InactiveAccountState.Closed, "Maria Santos", "5")]);

        Assert.Equal(["Status", "Source"], cut.FindAll(".ca-filters .fh-dd-cap").Select(c => c.TextContent.Trim()).ToArray());
        Assert.Empty(cut.FindAll(".ca-chip"));
        var export = cut.Find(".ca-export");
        Assert.Equal(["Print", "CSV", "Excel"], export.QuerySelectorAll(".ca-export-btn").Select(b => b.TextContent.Trim()).ToArray());
        Assert.Equal("group", export.GetAttribute("role"));
        Assert.Equal("Print or export the register", export.GetAttribute("aria-label"));
        Assert.DoesNotContain("Admin", cut.Find(".topbar-breadcrumb").TextContent);
        Assert.Contains("Closed Accounts", cut.Find(".topbar-title").TextContent);
    }

    [Fact]
    public void EverySourceThatCanHoldAnInactiveAccountIsListedByItsOwnName()
    {
        var cut = RenderRegister(
            [Stall(InactiveAccountState.Closed, "Maria Santos", "5")],
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2"),
            Space(ObligationKind.VegetableFruitSpaceRental, "Ana Lopez", "VF-9"),
            Space(ObligationKind.FiestaArawLotRental, "Pedro Cruz", "Lot 4", @event: LotRentalEvent.Fiesta, eventDate: new DateOnly(2025, 8, 15)));

        cut.FindAll(".fh-dd").Single(d => d.QuerySelector(".fh-dd-cap")!.TextContent.Trim() == "Source").QuerySelector(".fh-dd-trigger")!.Click();
        var options = cut.FindAll(".fh-dd-item").Select(i => i.TextContent.Trim()).ToArray();

        Assert.Contains("All sources", options);
        Assert.Contains("Kanmanggay", options);
        Assert.Contains("Vegetable / Fruits", options);
        Assert.Contains("Fiesta / Araw", options);
        Assert.DoesNotContain("KM", options); Assert.DoesNotContain("FA", options);
        Assert.Equal(3, cut.FindAll(".ca-obligation-source").Count);
        Assert.Equal(["Kanmanggay", "Vegetable / Fruits", "Fiesta / Araw"], cut.FindAll(".ca-obligation-source .ca-year-label").Select(l => l.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void ASpaceWhoseSignedTermRanOut_IsReadHereAsTermEnded_NotLeftOnTheCurrentRegister()
    {
        // Signed for one year from Oct 25, 2022; never closed. Like a stall whose term has run out, it is no longer a current holder.
        var ended = Space(ObligationKind.KanmanggaySpaceRental, "Lorenofs Brando", "5") with
        {
            ActiveTo = null, Arrangement = OccupancyArrangement.SignedContract,
            ContractEffectiveDate = new DateOnly(2022, 10, 25), ContractYears = 1
        };
        var current = Space(ObligationKind.KanmanggaySpaceRental, "Diosa Cortes", "3") with { ActiveTo = null };

        var cut = RenderRegister([], ended, current);

        var row = cut.FindAll(".ca-obligation-source tbody tr").Single(r => r.TextContent.Contains("Lorenofs Brando"));
        Assert.Contains("Term ended", row.TextContent);
        Assert.DoesNotContain("Diosa Cortes", cut.Find(".ca-obligation-source").TextContent);
    }

    [Fact]
    public void ARenewedSpacesEarlierTerm_IsItsOwnRenewedRow_WithThatTermsFiguresOnly_AndNoLifecycleAction()
    {
        // Renewed on Oct 25, 2025 and still current: the earlier term is a record of its own, like a renewed stall's earlier contract.
        var renewed = Space(ObligationKind.KanmanggaySpaceRental, "Lorenofs Brando", "5") with
        {
            ActiveTo = null, Arrangement = OccupancyArrangement.SignedContract, ContractEffectiveDate = new DateOnly(2025, 10, 25), ContractYears = 3,
            AssessedToDate = 9_000m, CollectedToDate = 2_000m, OutstandingToDate = 7_000m,
            Terms =
            [
                new(1, new DateOnly(2022, 10, 25), 1, new DateOnly(2023, 10, 24), new DateOnly(2025, 10, 24), "Lorenofs Brando", "KM-1", 6_000m, 1_000m, 5_000m, false),
                new(2, new DateOnly(2025, 10, 25), 3, new DateOnly(2028, 10, 24), null, "Lorenofs Brando", "KM-2", 3_000m, 1_000m, 2_000m, true)
            ]
        };

        var cut = RenderRegister([], renewed);

        var row = Assert.Single(cut.FindAll(".ca-obligation-source tbody tr"));            // the current term is not on the register
        Assert.Contains("Renewed", row.TextContent);
        Assert.Contains("Oct 25, 2022", row.TextContent);
        Assert.Contains("Oct 24, 2025", row.TextContent);
        Assert.Contains("₱5,000", row.TextContent);                                           // the earlier term's own balance, not the account's ₱7,000
        Assert.DoesNotContain("₱7,000", row.TextContent);
        Assert.Empty(row.QuerySelectorAll(".ca-row-renew, .ca-row-reopen, .ca-row-remove"));  // view only
    }

    [Fact]
    public void FilteringBySourceNarrowsTheRegisterToThatSourceAlone()
    {
        var cut = RenderRegister(
            [Stall(InactiveAccountState.Closed, "Maria Santos", "5")],
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2"),
            Space(ObligationKind.VegetableFruitSpaceRental, "Ana Lopez", "VF-9"));

        Choose(cut, "Source", "Kanmanggay");

        Assert.Contains("Lito Ramos", cut.Markup);
        Assert.DoesNotContain("Ana Lopez", cut.Markup);
        Assert.DoesNotContain("Maria Santos", cut.Markup);
    }

    [Fact]
    public void EventLotsKeepTheirEventAndAreNeverGivenARecurringContractOrALifecycleAction()
    {
        var cut = RenderRegister([],
            Space(ObligationKind.FiestaArawLotRental, "Pedro Cruz", "Lot 4", @event: LotRentalEvent.Araw, eventDate: new DateOnly(2025, 8, 15)),
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2"));

        var lots = cut.FindAll(".ca-obligation-source").Single(s => s.TextContent.Contains("Fiesta / Araw"));
        Assert.Contains("Closed event lot", lots.TextContent);
        Assert.Contains("Araw · Aug 15, 2025", lots.TextContent);
        Assert.Contains("Event", lots.QuerySelector("thead")!.TextContent);
        // Until the server's eligibility arrives a space row shows View only; its actions are links drawn from that eligibility (see the tests below).
        foreach (var source in cut.FindAll(".ca-obligation-source"))
        {
            Assert.Empty(source.QuerySelectorAll(".ca-row-renew, .ca-row-reopen, .ca-row-remove, .ca-renew-all-btn"));
            Assert.Single(source.QuerySelectorAll(".ca-row-view"));
            Assert.Equal("View account", source.QuerySelector(".ca-row-view")!.GetAttribute("aria-label"));
        }
    }


    private static ClosedStallAccountDto In(string name, FacilityCode code, InactiveAccountState state, string holder, string no, bool reLet = false) =>
        Stall(state, holder, no, reLet: reLet, facility: code, facilityName: name);

    private static AngleSharp.Dom.IElement FacilityGroup(IRenderedComponent<ClosedAccounts> cut, string name) =>
        cut.FindAll(".ca-fac-group").Single(g => g.QuerySelector(".ca-fac-title")!.TextContent.Trim() == name);

    [Fact]
    public void RenewEligibleIsOfferedPerFacility_NeverAsOneButtonForTheWholeYear()
    {
        var cut = RenderRegister(
            [In("Iceplant", FacilityCode.ICE, InactiveAccountState.Expired, "Ice A", "1"),
             In("Iceplant", FacilityCode.ICE, InactiveAccountState.Expired, "Ice B", "2"),
             In("New Public Market", FacilityCode.NPM, InactiveAccountState.Expired, "Npm A", "5"),
             In("New Public Market", FacilityCode.NPM, InactiveAccountState.Closed, "Npm Closed", "6"),
             In("New Public Market", FacilityCode.NPM, InactiveAccountState.Lapsed, "Npm Relet", "7", reLet: true),
             In("New Public Market", FacilityCode.NPM, InactiveAccountState.Renewed, "Npm Renewed", "8")],
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2"));

        Assert.Empty(cut.FindAll(".ca-year-head .ca-renew-all-btn"));                         // not at the year level
        Assert.Equal("Renew eligible (2)", FacilityGroup(cut, "Iceplant").QuerySelector(".ca-renew-all-btn")!.TextContent.Trim());
        // Closed, re-let and already-renewed accounts are not eligible; only the one Expired NPM account is.
        Assert.Equal("Renew eligible (1)", FacilityGroup(cut, "New Public Market").QuerySelector(".ca-renew-all-btn")!.TextContent.Trim());
        // Source-owned space accounts never get a bulk renewal.
        Assert.Empty(cut.Find(".ca-obligation-source").QuerySelectorAll(".ca-renew-all-btn"));
        // It is a link to the facility's own renewal page: the report executes nothing.
        Assert.Equal("a", FacilityGroup(cut, "Iceplant").QuerySelector(".ca-renew-all-btn")!.TagName.ToLowerInvariant());
        Assert.Equal("/facility/ice/renewals", FacilityGroup(cut, "Iceplant").QuerySelector(".ca-renew-all-btn")!.GetAttribute("href"));
        Assert.Equal("/facility/npm/renewals", FacilityGroup(cut, "New Public Market").QuerySelector(".ca-renew-all-btn")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll(".eemo-modal"));
    }



    [Fact]
    public void ThePeriodStatusAndSourceControlsShareOneClusterAtOneHeight_AndTheBalanceNoteSpansTheSection()
    {
        var cut = RenderRegister([Stall(InactiveAccountState.Closed, "Maria Santos", "5")]);

        var cluster = cut.Find(".ca-controls .ca-filters");
        Assert.NotNull(cluster.QuerySelector(".rpt-period-trigger"));
        Assert.Equal(2, cluster.QuerySelectorAll(".fh-dd").Length);
        Assert.Contains("Whole Time", cluster.TextContent);
        Assert.Empty(cut.FindAll(".ca-filter-cap"));
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EEMOCantilanSDS.Client", "Components", "Pages", "Reports", "ClosedAccounts.razor.css"));
        Assert.Matches(@"\.ca-filter ::deep \.rpt-period-trigger\s*\{[^}]*height:\s*42px", css);
        Assert.Matches(@"\.ca-why p\s*\{[^}]*max-width:\s*none", css);
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }

    [Fact]
    public void PrintCsvAndExcelDescribeTheSameFilteredRecords_WithSourceNamesNotCodes()
    {
        var cut = RenderRegister(
            [Stall(InactiveAccountState.Closed, "Maria Santos", "5", uncollected: 800m),
             Stall(InactiveAccountState.Lapsed, "Lapsed Holder", "6", uncollected: 300m)],
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2", outstanding: 100m));

        var all = Csv(cut);
        Assert.Contains("Maria Santos", all); Assert.Contains("Lapsed Holder", all); Assert.Contains("Lito Ramos", all);
        Assert.Contains("Kanmanggay", all);
        Assert.DoesNotContain(",KM,", all);
        Assert.Contains("Stated here only", all); Assert.Contains("Also in live follow-up", all);

        // Only closed accounts carry a source-owned space row, so a Lapsed view drops it from the register and the file alike.
        Choose(cut, "Status", "Lapsed");
        var lapsed = Csv(cut);
        Assert.Contains("Lapsed Holder", lapsed);
        Assert.DoesNotContain("Maria Santos", lapsed);
        Assert.DoesNotContain("Lito Ramos", lapsed);
        Assert.DoesNotContain("Lito Ramos", cut.Markup);
        Assert.Contains("Status,Lapsed", lapsed.Replace("\"", ""));
    }

    [Fact]
    public void EndedAndLapsedBalancesStayDistinct_AndSpaceBalancesAreNeverAddedToThem()
    {
        var cut = RenderRegister(
            [Stall(InactiveAccountState.Closed, "Maria Santos", "5", uncollected: 800m),
             Stall(InactiveAccountState.Lapsed, "Lapsed Holder", "6", uncollected: 300m)],
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2", outstanding: 100m));

        var cards = cut.FindAll(".ca-sum-card").ToDictionary(c => c.QuerySelector(".ca-sum-lbl")!.TextContent.Trim(), c => c.QuerySelector(".ca-sum-val")!.TextContent.Trim());

        Assert.Equal("₱800", cards["Stated here only"]);
        Assert.Equal("₱300", cards["Lapsed · already in follow-up"]);
        Assert.Equal("3", cards["Inactive Accounts"]);
        Assert.DoesNotContain("1,100", cut.Find(".ca-summary").TextContent);
        Assert.Contains("never added", cut.Find(".ca-note").TextContent);
        Assert.Contains("Balances stay in the account workspace", cut.Find(".ca-source-note").TextContent);
    }

    [Fact]
    public void RendersForVisualReview()
    {
        var cut = RenderRegister(
            [Stall(InactiveAccountState.Closed, "Maria Santos", "5", uncollected: 800m),
             Stall(InactiveAccountState.Expired, "Eligible Holder", "1"),
             Stall(InactiveAccountState.Lapsed, "Lapsed Holder", "6", uncollected: 300m)],
            Space(ObligationKind.KanmanggaySpaceRental, "Lito Ramos", "KM-2", outstanding: 100m),
            Space(ObligationKind.FiestaArawLotRental, "Pedro Cruz", "Lot 4", @event: LotRentalEvent.Fiesta, eventDate: new DateOnly(2025, 8, 15)));
        MonthlyCollectionReportPageTests.Export("closed-accounts", cut.Markup);
    }

    [Fact]
    public void TheCsvAndTheStylesheetAreWellFormed()
    {
        var css = File.ReadAllText("../../../../EEMOCantilanSDS.Client/Components/Pages/Reports/ClosedAccounts.razor.css");
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
        Assert.Contains(".ca-export", css);
        Assert.DoesNotContain(".ca-chip", css);
    }
    private static AccountLifecycleDto Lifecycle(Guid id, params AllowedAction[] actions) => new(LifecycleSource.SpaceRental, id, "Space 1", "KanmanggaySpaceRental", "Josephine", LifecycleState.Closed,
        new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 31), null, actions, []);

    [Fact]
    public void SpaceRowsShowTheSameCompactActionsAStallRowDoes_AsLinksToTheProfile_AndTheReportMutatesNothing()
    {
        var km = Space(ObligationKind.KanmanggaySpaceRental, "Josephine", "Space 1");
        var lot = Space(ObligationKind.FiestaArawLotRental, "Pedro Cruz", "Lot 4", @event: LotRentalEvent.Araw, eventDate: new DateOnly(2025, 8, 15));
        _lifecycleApi.Setup(a => a.GetManyAsync(LifecycleSource.SpaceRental, It.IsAny<IReadOnlyList<Guid>>())).ReturnsAsync(
            Result<IReadOnlyList<AccountLifecycleDto>>.Success([Lifecycle(km.Id,
                new(LifecycleAction.Resume, true, null, null, "Admin", true),
                new(LifecycleAction.Renew, false, "AccountClosed", "A closed account is resumed, not renewed.", "Admin", true),
                new(LifecycleAction.Reassign, true, null, null, "Admin", true),
                new(LifecycleAction.Remove, true, null, null, "SuperAdmin", true))]));
        var cut = RenderRegister([], km, lot);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ca-row-reopen")), Timeout);
        Assert.Equal($"/operations/kanmanggay/profile/{km.Id}?action=resume", cut.Find(".ca-row-reopen").GetAttribute("href"));
        Assert.Equal($"/operations/kanmanggay/profile/{km.Id}?action=reassign", cut.Find(".ca-row-renew").GetAttribute("href"));   // the Assign/Reassign icon, as on a stall row
        Assert.Equal($"/operations/kanmanggay/profile/{km.Id}?action=retire", cut.Find(".ca-row-remove").GetAttribute("href"));
        Assert.Equal(["Resume account", "Reassign holder", "Retire account"], cut.FindAll(".ca-obligation-source a.ca-row-icon:not(.ca-row-view)").Select(x => x.GetAttribute("aria-label")).ToArray());
        Assert.Empty(cut.FindAll(".ca-menu-row, .ca-menu-list"));                                              // no panel beneath the row
        Assert.All(cut.FindAll(".ca-obligation-source .ca-act"), x => Assert.Contains("no-print", x.ClassName));   // never printed
        Assert.Empty(cut.FindAll(".ca-obligation-source button[type='submit']"));
        // The event lot has its View link and nothing else.
        var lotRow = cut.FindAll(".ca-obligation-source").Single(s => s.TextContent.Contains("Fiesta / Araw"));
        Assert.Single(lotRow.QuerySelectorAll("a.ca-row-icon"));
        _lifecycleApi.Verify(a => a.GetManyAsync(LifecycleSource.SpaceRental, It.IsAny<IReadOnlyList<Guid>>()), Times.Once);   // one batched read, not one per row
    }

    [Fact]
    public void AnActionWaitingOnTheOneTimeSpaceSetup_IsADisabledIconWithATooltip_AndOthersAreNotDrawn()
    {
        var km = Space(ObligationKind.KanmanggaySpaceRental, "Josephine", "Space 1");
        _lifecycleApi.Setup(a => a.GetManyAsync(LifecycleSource.SpaceRental, It.IsAny<IReadOnlyList<Guid>>())).ReturnsAsync(
            Result<IReadOnlyList<AccountLifecycleDto>>.Success([Lifecycle(km.Id,
                new(LifecycleAction.Resume, false, "NoSpaceRegistration", "This account needs its one-time space setup first.", "Admin", true),
                new(LifecycleAction.Renew, false, "AccountClosed", "x", "Admin", true))]));
        var cut = RenderRegister([], km);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ca-row-off")), Timeout);
        var off = cut.Find(".ca-row-off");
        Assert.Equal("This account needs its one-time space setup first.", off.GetAttribute("title"));
        Assert.Equal("true", off.GetAttribute("aria-disabled"));
        Assert.Single(cut.FindAll(".ca-obligation-source .ca-row-icon:not(.ca-row-view)"));
    }

    [Fact]
    public void AFailedEligibilityRead_IsStatedOnce_WithARetry_AndTheRowsKeepTheirViewLink()
    {
        var km = Space(ObligationKind.KanmanggaySpaceRental, "Josephine", "Space 1");
        _lifecycleApi.Setup(a => a.GetManyAsync(It.IsAny<LifecycleSource>(), It.IsAny<IReadOnlyList<Guid>>())).ReturnsAsync(Result<IReadOnlyList<AccountLifecycleDto>>.Forbidden());
        var cut = RenderRegister([], km);
        cut.WaitForAssertion(() => Assert.Contains("Row actions could not be loaded", cut.Markup), Timeout);
        Assert.Contains("Try again", cut.Find(".ca-obligation-source .ca-source-note[role='status'] button").TextContent);
        Assert.Single(cut.FindAll(".ca-obligation-source a.ca-row-view"));
    }

    [Fact]
    public void AnAccountWhoseEndDateHasNotArrivedIsStillActive_SoItIsNotListedAsClosed()
    {
        var closed = Space(ObligationKind.KanmanggaySpaceRental, "Pony Tail", "Space 2");
        var ending = Space(ObligationKind.KanmanggaySpaceRental, "Josephine", "Space 1") with { ActiveTo = DateOnly.FromDateTime(DateTime.Today).AddDays(12) };
        var cut = RenderRegister([], closed, ending);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tr[data-account]")), Timeout);
        Assert.Contains("Pony Tail", cut.Markup);
        Assert.DoesNotContain("Josephine", cut.Markup);
    }
}
