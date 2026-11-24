using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Collections;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>
/// Collecting a stall's unpaid months also collects each month's own penalties (surcharge, interest) on the same receipt, shown under the month:
/// confirmed with the office before any money is recorded, then the rent month by month, then the penalties as one checkout.
/// </summary>
public sealed class ArrearsPenaltiesTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 11, 24);
    private readonly Guid _stall = Guid.NewGuid(), _contract = Guid.NewGuid(), _surcharge = Guid.NewGuid(), _interest = Guid.NewGuid();
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly List<PendingOperation> _queue = [];
    private readonly List<string> _calls = [];

    private IRenderedComponent<ArrearsMonths> Render(bool quoteRefuses = false)
    {
        _api.Setup(x => x.GetUnpaidMonthsAsync(_stall, _contract)).ReturnsAsync(Result<IReadOnlyList<MobileUnpaidMonthDto>>.Success(
            [new(2025, 9, 1200m, 0m, 1200m, true), new(2025, 10, 1200m, 0m, 1200m, true)]));
        CollectionSessionSourceChoice Fine(Guid id, string name, decimal amount) => new($"FINE|{id:N}", CollectionSessionItemKind.FineAssessment,
            "PENALTIES", name, "October 2025", new(FineAssessmentId: id, FineVersion: 1), RevenueInstrumentType.OfficialReceipt,
            CollectionSessionAmountRule.PreparedBalance, ServerAmount: amount, MaximumAmount: amount);
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(It.Is<CollectionSourceIdentity?>(i => i!.Kind == SourceIdentityKind.Occupancy && i.Id == _contract)))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
                [new(CollectionSessionItemKind.FineAssessment, "PENALTIES", "Fines / Penalties", true, true, null, null, false, [], true,
                    [Fine(_interest, "Interest", 30m), Fine(_surcharge, "Surcharge", 300m)])])));
        _api.Setup(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>()))
            .Callback(() => _calls.Add("quote"))
            .ReturnsAsync((CollectionSessionIntent i) => Result<CollectionSessionQuote>.Success(new(i.ClientCollectionSessionId, null, i.BusinessDate,
                i.Items.Select(x => new CollectionSessionItemQuote(x.ClientItemId, x.Kind, "PENALTIES", "Penalties", "",
                    RevenueInstrumentType.OfficialReceipt, x.ConfirmedAmount, "v1", Guid.Empty)).ToArray(),
                [], i.Items.Sum(x => x.ConfirmedAmount), quoteRefuses ? null : "fp-1",
                quoteRefuses ? [new(null, "BalanceChanged", "The fine balance changed.")] : [])));
        _api.Setup(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()))
            .Callback<RecordMobileMonthlyCollectionRequest>(r => _calls.Add($"rent {r.BillingYear}-{r.BillingMonth}"))
            .ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(_api.Object);

        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => _queue.ToList());
        store.Setup(x => x.AddIssuedDocumentOperationAsync(It.IsAny<PendingOperation>())).Callback<PendingOperation>(o => { _calls.Add("queue"); _queue.Add(o); }).Returns(Task.CompletedTask);
        store.Setup(x => x.UpdateAsync(It.IsAny<PendingOperation>())).Returns(Task.CompletedTask);
        var online = new Mock<IConnectivityMonitor>();
        online.SetupGet(x => x.IsOnline).Returns(true);
        var collector = new Mock<ICurrentCollectorProvider>();
        collector.SetupGet(x => x.CollectorKey).Returns("collector-1");
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, online.Object, collector.Object));
        Services.AddSingleton(new MobileSessionService { Menu = new(Guid.NewGuid(), "Scratch collector", "test", Today,
            [new(FacilityCode.NCC, "Scratch Center", "Monthly", true, true)]) });
        Services.AddSingleton(new ArrearsWorkspaceHolder());
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/arrears/months?stall={_stall}&contract={_contract}&space=Stall%20D-01");
        return RenderComponent<ArrearsMonths>();
    }

    private static void ClickButton(IRenderedComponent<ArrearsMonths> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    private void AddBothMonths(IRenderedComponent<ArrearsMonths> cut)
    {
        cut.WaitForAssertion(() => cut.Find(".collection-available-fees button"));
        Assert.Contains("2 unpaid months · ₱2,400.00 + ₱330.00 penalties", cut.Find(".collection-available-fees button").TextContent);
        cut.Find(".collection-available-fees button").Click();
        // Each month in the picker reads as its total with what it is made of; a month without penalties is just its rent.
        var october = cut.FindAll(".smv-row").Single(r => r.TextContent.Contains("October"));
        Assert.Contains("₱1,530.00", october.TextContent);
        Assert.Equal("Rent ₱1,200.00 + penalties ₱330.00", october.QuerySelector(".smv-detail")!.TextContent.Trim());
        Assert.Null(cut.FindAll(".smv-row").Single(r => r.TextContent.Contains("September")).QuerySelector(".smv-detail"));
        cut.Find(".smv-year").Click();
        ClickButton(cut, "Add 2 months");
    }

    [Fact]
    public void Each_month_shows_its_penalties_and_the_receipt_records_rent_then_penalties_after_confirming_them()
    {
        var cut = Render();
        AddBothMonths(cut);

        // Each month: its total, then (when it has penalties) rent, surcharge and interest as their own rows. No instrument label repeated on every row.
        var months = cut.FindAll(".month-item");
        Assert.Equal(["₱1,200.00", "₱1,530.00"], months.Select(r => r.QuerySelector(".month-total")!.TextContent.Trim()));
        Assert.Null(months[0].QuerySelector(".month-lines"));
        Assert.Equal([("Rent", "₱1,200.00"), ("Surcharge", "₱300.00"), ("Interest", "₱30.00")],
            months[1].QuerySelectorAll(".month-lines > div").Select(r => (r.QuerySelector("dt")!.TextContent.Trim(), r.QuerySelector("dd")!.TextContent.Trim())));
        Assert.DoesNotContain("OR", months[1].TextContent);

        cut.Find("input.form-input[type=text]").Change("123456");
        ClickButton(cut, "Review collection");
        Assert.Equal(["Rent₱2,400.00", "Surcharge₱300.00", "Interest₱30.00", "Total₱2,730.00"],
            cut.FindAll(".review-summary .review-row").Select(r => r.TextContent.Replace(" ", "").Replace("\n", "").Trim()));

        ClickButton(cut, "Record collection");
        cut.WaitForAssertion(() => Assert.Contains("queue", _calls));
        Assert.Equal(["quote", "rent 2025-9", "rent 2025-10", "queue"], _calls);
        var checkout = Assert.Single(_queue).CollectionSession!;
        Assert.Equal("123456", checkout.Intent.ReceiptReference);
        Assert.Equal(new CollectionSourceIdentity(SourceIdentityKind.Occupancy, _contract), checkout.Intent.SourceIdentity);
        Assert.Equal([(_interest, 30m), (_surcharge, 300m)], checkout.Intent.Items.Select(i => (i.Fine!.AssessmentId, i.ConfirmedAmount)));
    }

    [Fact]
    public void Penalties_the_office_cannot_confirm_record_nothing()
    {
        var cut = Render(quoteRefuses: true);
        AddBothMonths(cut);
        ClickButton(cut, "Review collection");
        ClickButton(cut, "Record collection");

        cut.WaitForAssertion(() => Assert.Contains("Nothing was recorded", cut.Find(".review-problem").TextContent));
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
        Assert.Empty(_queue);
    }
}
