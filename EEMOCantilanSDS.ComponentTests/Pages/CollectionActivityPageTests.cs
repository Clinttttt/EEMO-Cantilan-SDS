using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TransactionsPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Transactions;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Collection Activity reads the unified server feed (each real collection once, legacy and canonical) — one call per day,
/// filtered in place by source scope — and never concatenates per-source APIs in the browser.
/// </summary>
public sealed class CollectionActivityPageTests : TestContext
{
    private readonly Mock<ICollectionActivityApiClient> _activity = new();
    private IReadOnlyList<global::EEMOCantilanSDS.Application.Common.Revenue.CollectionSessionDisplayGroup> _sessions = [];
    private readonly Mock<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IDirectCollectionReviewApiClient> _review = new();

    [Theory]
    [InlineData(null, RevenueInstrumentType.OfficialReceipt)]
    [InlineData("OR-123", RevenueInstrumentType.OfficialReceipt)]
    [InlineData("CT-123", RevenueInstrumentType.CashTicket)]
    public void Receipt_is_the_recorded_document_and_SRC_stays_a_separate_audit_reference(string? number, RevenueInstrumentType instrument)
    {
        var page = RenderPage(Event("receipt", "Canonical", "PERMANENT_STALL_RENT", "Rent", 30m,
            instrument: instrument, document: number));
        page.WaitForAssertion(() => Assert.Contains(number ?? "None recorded", page.Find(".txn-or").TextContent));
        Assert.DoesNotContain("SRC-", page.Find(".txn-or").TextContent);
        page.Find(".txn-row").Click();
        Assert.Contains("Receipt", page.Find(".txr-meta").TextContent);
        Assert.Contains(number ?? "None recorded", page.Find(".txr-meta").TextContent);
        Assert.Contains("StallTrack reference", page.Find(".txr-meta").TextContent);
        Assert.Contains("SRC-2026-000127", page.Find(".txr-meta").TextContent);
    }

    private static CollectionActivityEventDto Event(string key, string authority, string code, string name, decimal amount,
        FacilityCode? facility = null, RevenueInstrumentType? instrument = null, string? document = null,
        IReadOnlyList<CollectionActivityLineDto>? lines = null) =>
        new(key, authority, authority == "Canonical" ? "Collection" : "PaymentRecord", null, PhilippineTime.Today,
            DateTime.UtcNow, authority == "Canonical" ? "SRC-2026-000127" : null, document, instrument, [], [], "Juan Dela Cruz", null, null, null, "office", facility, null,
            authority == "Legacy" ? "Paid" : null, amount, 0m, amount, authority == "Legacy" ? "Recorded" : "Posted", null,
            lines ?? [new CollectionActivityLineDto(code, name, amount, null, null, null, null, null, null)], []);

    private IRenderedComponent<TransactionsPage> RenderPage(params CollectionActivityEventDto[] events)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _activity
            .Setup(client => client.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync((DateOnly from, DateOnly to, FacilityCode? _, Guid? _, string? _, int _, string? _) =>
                Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(from, to, "basis", "LatestCorrected",
                    DateTime.UtcNow, events, events.Length, false, 0m, events.Sum(x => x.Amount), 0m, events.Sum(x => x.Amount), [], _sessions)));

        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(client => client.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                Array.Empty<FacilitySidebarSummaryDto>()));

        Services.AddSingleton(_activity.Object);
        _review.Setup(x => x.GetAsync(It.IsAny<EEMOCantilanSDS.Application.Dtos.Revenue.DirectCollectionReviewRequest>()))
            .ReturnsAsync(Result<EEMOCantilanSDS.Application.Dtos.Revenue.DirectCollectionReviewPageDto>.Success(new([], 0, 2)));
        Services.AddSingleton(_review.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IOfficeSourcesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<TransactionsPage>();
    }

    [Fact]
    public void Same_checkout_shows_one_parent_and_itemized_separate_SRCs_while_search_keeps_child_identity()
    {
        var collector = Guid.NewGuid();
        var first = Event("a", "Canonical", "FISH_MEAT_VENDOR_FEE", "Fish/Meat", 56m,
            instrument: RevenueInstrumentType.OfficialReceipt, document: "OR-100")
            with { CollectionId = Guid.NewGuid(), CollectorId = collector, ReferenceCode = "SRC-2026-000221" };
        var second = Event("b", "Canonical", "WEIGHT_AND_MEASURE", "Weight", 132m,
            instrument: RevenueInstrumentType.OfficialReceipt, document: "OR-101")
            with { CollectionId = Guid.NewGuid(), CollectorId = collector, ReferenceCode = "SRC-2026-000222" };
        var third = Event("c", "Canonical", "WCF", "Water", 284m,
            instrument: RevenueInstrumentType.CashTicket, document: "CT-111")
            with
            {
                CollectionId = Guid.NewGuid(), CollectorId = collector, ReferenceCode = "SRC-2026-000223",
                CorrectionEffect = -284m, NetAmount = 0m, Disposition = "Reversed",
                Corrections = [new(Guid.NewGuid(), "Reversal", PhilippineTime.Today, DateTime.UtcNow, -284m, null, "Duplicate payment correction")]
            };
        _sessions = [new(Guid.NewGuid(), [first.CollectionId!.Value, second.CollectionId!.Value, third.CollectionId!.Value])];
        var page = RenderPage(first, second, third);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".txn-row")));
        Assert.Contains("472.00", page.Find(".txn-amount").TextContent);
        Assert.Contains("188.00", page.Find(".txn-net").TextContent);
        Assert.Contains("Contains corrections", page.Find(".txn-row").TextContent);
        page.Find(".txn-row").Click();
        Assert.Contains("SRC-2026-000221", page.Find("[aria-label='Itemized checkout collections']").TextContent);
        Assert.Contains("SRC-2026-000222", page.Find("[aria-label='Itemized checkout collections']").TextContent);
        Assert.Contains("SRC-2026-000223", page.Find("[aria-label='Itemized checkout collections']").TextContent);
        Assert.Contains("CT-111", page.Find("[aria-label='Itemized checkout collections']").TextContent);
        page.FindAll(".txr-foot button").Last().Click();
        page.Find("input.txn-search-input").Input("SRC-2026-000222");
        Assert.Single(page.FindAll(".txn-row"));
        Assert.Contains("132.00", page.Find(".txn-row").TextContent);
        Assert.DoesNotContain("Combined collection", page.Find(".txn-row").TextContent);
    }

    [Fact]
    public void RendersCollectionActivityIdentityWithoutAnAuditTrailLinkOrCollectionDateClaim()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Collection Activity", page.Find("h1.v3h-title").TextContent.Trim());
            Assert.Equal("Collection Activity", page.Find(".topbar-title").TextContent.Trim());
            Assert.Equal("Posted collections and recorded legacy payments.", page.Find(".v3h-sub").TextContent.Trim());
            var emptyState = page.Find(".txn-empty").TextContent;
            Assert.Contains("No collection activity", emptyState, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));

        var markup = page.Markup.ToLowerInvariant();
        Assert.DoesNotContain("/audit-trail", markup);
        Assert.DoesNotContain("collection date", markup);
        Assert.DoesNotContain("financial reports", markup);
    }

    [Fact]
    public void ScopeSourceDateRefreshAndSearchShareOneToolbar_AndSearchStillClears()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".txn-controls")), TimeSpan.FromSeconds(5));

        var row = page.Find(".txn-controls");
        Assert.NotNull(row.QuerySelector(".txn-segment"));                                          // category tabs
        Assert.NotNull(row.QuerySelector(".txn-source .fh-dd-trigger"));                            // Source
        Assert.NotNull(row.QuerySelector(".txn-search"));                                           // search, in the same row rather than on a line of its own
        Assert.NotNull(row.QuerySelector(".txn-nav .txn-daynav"));
        Assert.NotNull(row.QuerySelector(".txn-nav .txn-refresh"));
        Assert.Equal(new[] { "txn-segment", "txn-source", "txn-nav", "txn-search-row" },
            row.Children.Select(child => child.ClassName));
        // The compact review control sits immediately after the search, in the same row, as an icon with an accessible name.
        var searchRow = row.QuerySelector(".txn-search-row")!;
        Assert.Equal(new[] { "txn-search", "v3-btn dcr-trigger" }, searchRow.Children.Select(c => c.ClassName));
        Assert.StartsWith("Collection Review", searchRow.QuerySelector(".dcr-trigger")!.GetAttribute("aria-label"));
        Assert.NotNull(page.Find(".txn-nav .txn-daynav"));
        Assert.NotNull(page.Find(".txn-search .txn-search-icon"));
        Assert.Equal("Search SRC or payer", page.Find("input.txn-search-input").GetAttribute("placeholder"));
        Assert.Empty(page.FindAll(".txn-search-clear"));                                            // nothing to clear yet

        page.Find("input.txn-search-input").Input("juan");

        var clear = page.Find(".txn-search-clear");
        Assert.Equal("Clear search", clear.GetAttribute("aria-label"));
        clear.Click();
        Assert.Empty(page.FindAll(".txn-search-clear"));
    }

    [Fact]
    public void ACompleteSrcNotOnTheLoadedDay_IsLookedUpOnTheServer_AndThePageJumpsToItsDay()
    {
        var other = PhilippineTime.Today.AddDays(-9);
        var hit = Event("far", "Canonical", "MARKET_FEES", "Market Fees", 25m) with { ReferenceCode = "SRC-2026-000127", BusinessDate = other };
        var page = RenderPage();   // today is empty
        _activity.Setup(c => c.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), "SRC-2026-000127"))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(other, other, "b", "LatestCorrected",
                DateTime.UtcNow, [hit], 1, false, 0m, 25m, 0m, 25m, [])));
        _activity.Setup(c => c.GetAsync(other, other, null, null, null, It.IsAny<int>(), null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(other, other, "b", "LatestCorrected",
                DateTime.UtcNow, [hit], 1, false, 0m, 25m, 0m, 25m, [])));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".txn-empty")), TimeSpan.FromSeconds(5));

        page.Find("input.txn-search-input").Input("SRC-2026-000127");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("None recorded", page.Find("table.txn-table tbody").TextContent);
            Assert.Contains("SRC-2026-000127", page.Find(".txn-row").GetAttribute("aria-label"));
            Assert.Contains(other.ToString("MMMM d, yyyy"), page.Find(".txn-datebox-sub").TextContent);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void LoadsTheUnifiedFeedOncePerDay_AndTheDayNavigatorMovesIt()
    {
        var page = RenderPage();
        var selectedDay = DateOnly.Parse(page.Find("input[type='date']").GetAttribute("value")!);

        page.WaitForAssertion(
            () => _activity.Verify(client => client.GetAsync(selectedDay, selectedDay, null, null, null, 1000), Times.Once),
            TimeSpan.FromSeconds(5));

        // A scope is a filter over the loaded day, not another request.
        page.FindAll("button.txn-seg-tab").Single(button => button.TextContent.Trim() == "Operations").Click();
        _activity.Verify(client => client.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>(), null), Times.Once);

        page.Find("button[aria-label='Previous day']").Click();
        page.WaitForAssertion(
            () => _activity.Verify(client => client.GetAsync(selectedDay.AddDays(-1), selectedDay.AddDays(-1), null, null, null, 1000), Times.Once),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ListsFacilityOperationAndUtilityCollections_WithProfessionalNames_AndScopesThem()
    {
        var page = RenderPage(
            Event("PaymentRecord:1", "Legacy", "PERMANENT_STALL_RENT", "Permanent Stall Rent", 900m, FacilityCode.TCC, document: "LEGACY-1"),
            Event("Collection:2", "Canonical", "LANDING_BERTHING", "LANDING_BERTHING", 50m, instrument: RevenueInstrumentType.CashTicket, document: "CT-0001"),
            Event("Collection:3", "Canonical", "WCF", "WCF", 120m, FacilityCode.NPM, RevenueInstrumentType.CashTicket, "CT-0002"),
            Event("Collection:4", "Canonical", "MARKET_FEES", "Market Fees", 30m, instrument: RevenueInstrumentType.CashTicket, document: "CT-0003"));

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".txn-table tbody tr").Count), TimeSpan.FromSeconds(5));
        var recordFigure = page.FindAll(".v3h-figure").First().TextContent;
        Assert.Contains("Records", recordFigure); // the mixed feed includes legacy payments, not only posted Collections
        Assert.Contains("4", recordFigure);
        Assert.DoesNotContain("Collections", recordFigure);
        var markup = page.Markup;
        Assert.Contains("Landing / Berthing", markup);
        Assert.Contains("Water (WCF)", markup);
        Assert.DoesNotContain("LANDING_BERTHING", markup);

        page.FindAll("button.txn-seg-tab").Single(button => button.TextContent.Trim() == "Utilities").Click();
        var row = Assert.Single(page.FindAll(".txn-table tbody tr"));
        Assert.Contains("CT-0002", row.TextContent);

        page.FindAll("button.txn-seg-tab").Single(button => button.TextContent.Trim() == "Operations").Click();
        Assert.Equal(2, page.FindAll(".txn-table tbody tr").Count);
    }

    [Theory]
    [InlineData(false, "Slaughterhouse")]
    [InlineData(true, "Municipal Abattoir")]
    public void SlaughterhouseSourceIsSeparateFromFrozenChargeNames(bool tenantNamed, string sourceName)
    {
        var receipt = Event("Collection:21", "Canonical", "SLAUGHTERHOUSE", "Slaughterhouse", 170m,
            lines:
            [
                new("SLAUGHTERHOUSE", "Ante-mortem Inspection", 20m, "GovernedService", null, null, null, null, null, tenantNamed ? sourceName : null),
                new("SLAUGHTERHOUSE", "Slaughter Permit", 100m, "GovernedService", null, null, null, null, null, tenantNamed ? sourceName : null),
                new("SLAUGHTERHOUSE", "Slaughter Fee", 50m, "GovernedService", null, null, null, null, null, tenantNamed ? sourceName : null)
            ]) with { ReferenceCode = "SRC-2026-000021", PayerName = "Ana Reyes" };
        var page = RenderPage(receipt);
        page.WaitForAssertion(() => Assert.Equal(sourceName, page.Find(".txn-source-name").TextContent));
        Assert.Contains("3 items", page.Find(".txn-table tbody").TextContent);
        Assert.Contains("None recorded", page.Find(".txn-table tbody").TextContent);
        page.Find(".txn-source .fh-dd-trigger").Click();
        Assert.Contains(sourceName, page.Find(".txn-source").TextContent);
        Assert.DoesNotContain("Ante-mortem Inspection", page.Find(".txn-source").TextContent);
        page.Find(".txn-row").Click();
        Assert.Contains("SRC-2026-000021", page.Find(".txr-meta").TextContent);
        var items = page.FindAll(".txr-items tbody tr");
        Assert.Equal(new[] { "Ante-mortem Inspection", "Slaughter Permit", "Slaughter Fee" },
            items.Select(row => row.QuerySelector("td")!.TextContent));
        Assert.Equal(new[] { "₱20.00", "₱100.00", "₱50.00" },
            items.Select(row => row.QuerySelector("td.txn-num")!.TextContent));
        Assert.Contains("₱170.00", page.Find(".txr-items tfoot").TextContent);
        Assert.Equal(sourceName, page.FindAll(".txr-meta dd")[1].TextContent);
    }

    [Fact]
    public void LegacySlaughterhouseKeepsItsRecordedAnimalDescription()
    {
        var page = RenderPage(Event("SlaughterTransaction:1", "Legacy", "SLAUGHTERHOUSE", "Slaughterhouse", 50m,
            lines: [new("SLAUGHTERHOUSE", "Slaughterhouse", 50m, "SlaughterTransaction", null, null, null, null, "Cattle · 1 head")]));
        page.WaitForAssertion(() => Assert.Equal("Slaughterhouse", page.Find(".txn-source-name").TextContent));
        page.Find(".txn-row").Click();
        Assert.Equal("Cattle · 1 head", page.Find(".txr-items tbody td").TextContent);
    }

    [Fact]
    public void AnItemizedReceipt_IsOneRow_ThatOpensItsItemizedDetails()
    {
        var page = RenderPage(Event("Collection:9", "Canonical", "ICE_PLANT", "Ice Plant", 550m, FacilityCode.ICE,
            RevenueInstrumentType.OfficialReceipt, "OR-0001",
            [
                new CollectionActivityLineDto("ICE_PLANT", "Ice Plant", 250m, "PaymentRecord", null, null, 2026, 10, "Stall ICE-01"),
                new CollectionActivityLineDto("ICE_PLANT", "Ice Plant", 300m, "PaymentRecord", null, null, 2026, 9, "Stall ICE-01")
            ]));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".txn-table tbody tr")), TimeSpan.FromSeconds(5));
        page.Find(".txn-row").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".txr-items tbody tr").Count));
        Assert.Contains("Sep 2026", page.Markup);
    }
}
