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
            .Setup(client => client.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>()))
            .ReturnsAsync((DateOnly from, DateOnly to, FacilityCode? _, Guid? _, string? _, int _) =>
                Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(from, to, "basis", "LatestCorrected",
                    DateTime.UtcNow, events, events.Length, false, 0m, 0m, 0m, 0m, [])));

        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(client => client.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                Array.Empty<FacilitySidebarSummaryDto>()));

        Services.AddSingleton(_activity.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<TransactionsPage>();
    }

    [Fact]
    public void RendersCollectionActivityIdentityWithoutAnAuditTrailLinkOrCollectionDateClaim()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Collection Activity", page.Find("h1.v3h-title").TextContent.Trim());
            Assert.Equal("Collection Activity", page.Find(".topbar-title").TextContent.Trim());
            Assert.Equal("Recorded collection activity across StallTrack.", page.Find(".v3h-sub").TextContent.Trim());
            var emptyState = page.Find(".txn-empty").TextContent;
            Assert.Contains("No collection activity", emptyState, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));

        var markup = page.Markup.ToLowerInvariant();
        Assert.DoesNotContain("/audit-trail", markup);
        Assert.DoesNotContain("collection date", markup);
        Assert.DoesNotContain("financial reports", markup);
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
        _activity.Verify(client => client.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null, It.IsAny<int>()), Times.Once);

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

    [Fact]
    public void AnItemizedReceipt_IsOneRow_ThatExpandsToItsLines()
    {
        var page = RenderPage(Event("Collection:9", "Canonical", "ICE_PLANT", "Ice Plant", 550m, FacilityCode.ICE,
            RevenueInstrumentType.OfficialReceipt, "OR-0001",
            [
                new CollectionActivityLineDto("ICE_PLANT", "Ice Plant", 250m, "PaymentRecord", null, null, 2026, 10, "Stall ICE-01"),
                new CollectionActivityLineDto("ICE_PLANT", "Ice Plant", 300m, "PaymentRecord", null, null, 2026, 9, "Stall ICE-01")
            ]));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".txn-table tbody tr")), TimeSpan.FromSeconds(5));
        page.Find("button.txn-expand").Click();
        Assert.Equal(2, page.FindAll("tr.txn-subrow").Count);
        Assert.Contains("Sep 2026", page.Markup);
    }
}
