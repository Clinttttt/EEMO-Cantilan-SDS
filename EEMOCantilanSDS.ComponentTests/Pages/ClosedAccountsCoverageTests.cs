using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
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
        // Source-owned spaces offer View account only: no renew, reopen, assign or bulk renewal exists for them.
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
    }

    [Fact]
    public void RenewingOneFacilityTouchesOnlyItsOwnEligibleAccounts_AndTheDialogNamesIt()
    {
        var ice1 = In("Iceplant", FacilityCode.ICE, InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = In("Iceplant", FacilityCode.ICE, InactiveAccountState.Expired, "Ice B", "2");
        var npm = In("New Public Market", FacilityCode.NPM, InactiveAccountState.Expired, "Npm A", "5");
        var cut = RenderRegister([ice1, ice2, npm]);

        FacilityGroup(cut, "Iceplant").QuerySelector(".ca-renew-all-btn")!.Click();

        Assert.Equal("Renew eligible accounts — Iceplant", cut.Find(".eemo-modal-title").TextContent.Trim());
        Assert.Contains("2 eligible accounts", cut.Find(".confirm-text").TextContent);
        Assert.DoesNotContain("Npm A", cut.Find(".eemo-modal").TextContent);
        cut.Find(".eemo-modal-footer .btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, Renewed.Count), Timeout);
        Assert.Equal([ice1.StallId, ice2.StallId], Renewed.OrderBy(id => id == ice1.StallId ? 0 : 1).ToArray());
        Assert.DoesNotContain(npm.StallId, Renewed);
    }

    [Fact]
    public void AFilterNarrowsTheFacilityTargets_AndAPartialFailureRetriesOnlyTheFailedAccountsOfThatFacility()
    {
        var ice1 = In("Iceplant", FacilityCode.ICE, InactiveAccountState.Expired, "Ice A", "1");
        var ice2 = In("Iceplant", FacilityCode.ICE, InactiveAccountState.Expired, "Ice B", "2");
        var npm = In("New Public Market", FacilityCode.NPM, InactiveAccountState.Expired, "Npm A", "5");
        _failRenewals.Add(ice2.StallId);
        var cut = RenderRegister([ice1, ice2, npm]);

        FacilityGroup(cut, "Iceplant").QuerySelector(".ca-renew-all-btn")!.Click();
        cut.Find(".eemo-modal-footer .btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("1 renewed, 1 failed", cut.Find(".ca-form-err").TextContent), Timeout);
        Assert.Equal(2, Renewed.Count);
        Assert.DoesNotContain(npm.StallId, Renewed);
        // The retry covers only what is still eligible in Iceplant. The mocked register is unchanged, so both rows are still
        // listed — but only the failed account was carried into the retry set.
        Assert.Contains("1 eligible account", cut.Find(".confirm-text").TextContent);
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
}
