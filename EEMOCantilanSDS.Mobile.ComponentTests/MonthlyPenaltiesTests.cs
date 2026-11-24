using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>
/// Monthly billing collects the month's rent and the account's open penalties (surcharge, interest) on one receipt, with no choosing:
/// the penalties are confirmed with the server before any money is recorded, the rent keeps its own writer, and the penalties are one
/// checkout through the account's fine source under the same receipt number.
/// </summary>
public sealed class MonthlyPenaltiesTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 11, 24);
    private readonly Guid _stall = Guid.NewGuid(), _contract = Guid.NewGuid(), _surcharge = Guid.NewGuid(), _interest = Guid.NewGuid();
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly List<PendingOperation> _queue = [];
    private readonly List<string> _calls = [];

    private IRenderedComponent<MonthlyCollection> Render(PaymentStatus status, decimal penaltiesDue, bool quoteRefuses = false)
    {
        var paid = status == PaymentStatus.Paid ? 1200m : 0m;
        var row = new MobileMonthlyStallCollectionDto(_stall, "2", "Ruel Ramirez", "Ruel Ramirez", "", 1200m, status, paid, 1200m - paid,
            null, status != PaymentStatus.Unpaid, ContractId: _contract, PenaltiesDue: penaltiesDue);
        _api.Setup(x => x.GetMonthlyCollectionAsync(FacilityCode.NCC, 2026, 11)).ReturnsAsync(Result<MobileMonthlyCollectionDto>.Success(
            new(FacilityCode.NCC, "Scratch Center", 2026, 11, Today, 1, status == PaymentStatus.Paid ? 1 : 0, 0,
                status == PaymentStatus.Unpaid ? 1 : 0, paid, 1200m - paid, [row])));
        _api.Setup(x => x.GetUnpaidMonthsAsync(_stall, null)).ReturnsAsync(Result<IReadOnlyList<MobileUnpaidMonthDto>>.Success([]));

        CollectionSessionSourceChoice Fine(Guid id, string name, decimal amount) => new($"FINE|{id:N}", CollectionSessionItemKind.FineAssessment,
            "PENALTIES", name, "November 2026", new(FineAssessmentId: id, FineVersion: 1), RevenueInstrumentType.OfficialReceipt,
            CollectionSessionAmountRule.PreparedBalance, ServerAmount: amount, MaximumAmount: amount);
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(It.Is<CollectionSourceIdentity?>(i => i!.Kind == SourceIdentityKind.Occupancy && i.Id == _contract)))
            .Callback(() => _calls.Add("discover"))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
                [new(CollectionSessionItemKind.FineAssessment, "PENALTIES", "Fines / Penalties", true, true, null, null, false, [], true,
                    [Fine(_surcharge, "Surcharge", 300m), Fine(_interest, "Interest", 30m)])])));
        _api.Setup(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>()))
            .Callback(() => _calls.Add("quote"))
            .ReturnsAsync((CollectionSessionIntent i) => Result<CollectionSessionQuote>.Success(new(i.ClientCollectionSessionId, null, i.BusinessDate,
                i.Items.Select(x => new CollectionSessionItemQuote(x.ClientItemId, x.Kind, "PENALTIES", "Fines / Penalties", "",
                    RevenueInstrumentType.OfficialReceipt, x.ConfirmedAmount, "v1", Guid.Empty)).ToArray(),
                [], i.Items.Sum(x => x.ConfirmedAmount), quoteRefuses ? null : "fp-1",
                quoteRefuses ? [new(null, "BalanceChanged", "The fine balance changed.")] : [])));
        _api.Setup(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()))
            .Callback(() => _calls.Add("rent"))
            .ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(_api.Object);

        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => _queue.ToList());
        store.Setup(x => x.AddAsync(It.IsAny<PendingOperation>())).Callback<PendingOperation>(_queue.Add).Returns(Task.CompletedTask);
        store.Setup(x => x.AddIssuedDocumentOperationAsync(It.IsAny<PendingOperation>())).Callback<PendingOperation>(o => { _calls.Add("queue"); _queue.Add(o); }).Returns(Task.CompletedTask);
        store.Setup(x => x.UpdateAsync(It.IsAny<PendingOperation>())).Returns(Task.CompletedTask);
        var online = new Mock<IConnectivityMonitor>();
        online.SetupGet(x => x.IsOnline).Returns(true);
        var collector = new Mock<ICurrentCollectorProvider>();
        collector.SetupGet(x => x.CollectorKey).Returns("collector-1");
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, online.Object, collector.Object));
        Services.AddSingleton(new MobileSessionService { Menu = new(Guid.NewGuid(), "Scratch collector", "test", Today,
            [new(FacilityCode.NCC, "Scratch Center", "Monthly", true, true)]) });
        Services.AddSingleton(new MobilePaymentHubService());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return RenderComponent<MonthlyCollection>(p => p.Add(x => x.Facility, "ncc"));
    }

    private static AngleSharp.Dom.IElement Sheet(IRenderedComponent<MonthlyCollection> cut, string title) =>
        cut.FindAll(".sheet").Single(x => x.QuerySelector(".sheet-title")?.TextContent.Trim() == title);

    [Fact]
    public void Unpaid_rent_with_penalties_shows_the_statement_total_and_records_both_on_one_receipt()
    {
        var cut = Render(PaymentStatus.Unpaid, 330m);
        cut.WaitForAssertion(() => Assert.Contains("Penalties ₱330", cut.Find(".tx-card").TextContent));

        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Contains("Total amount due", Sheet(cut, "Record Monthly Payment").TextContent));
        var sheet = Sheet(cut, "Record Monthly Payment");
        var rows = sheet.QuerySelectorAll("dl[aria-label='Penalties'] .money-row").Select(r => r.TextContent.Trim()).ToList();
        Assert.Equal(["Surcharge₱300", "Interest₱30", "Total amount due₱1,530"], rows);
        Assert.Contains("Rent and penalties on one receipt - ₱1,530", sheet.TextContent);

        sheet.QuerySelector("input[placeholder='000001']")!.Input("123456");
        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("queue", _calls));
        // Penalties confirmed first, then the rent, then the penalties saved: a refusal of either leaves nothing half-recorded.
        Assert.Equal(["discover", "quote", "rent", "queue"], _calls);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r =>
            r.StallId == _stall && r.Status == PaymentStatus.Paid && r.PartialAmount == null && r.ORNumber == "123456")), Times.Once);
        var checkout = Assert.Single(_queue).CollectionSession!;
        Assert.Equal("fp-1", checkout.QuoteFingerprint);
        Assert.Equal("123456", checkout.Intent.ReceiptReference);
        Assert.Equal(new CollectionSourceIdentity(SourceIdentityKind.Occupancy, _contract), checkout.Intent.SourceIdentity);
        Assert.Equal(Today, checkout.Intent.BusinessDate);
        Assert.Equal([(_surcharge, 300m), (_interest, 30m)], checkout.Intent.Items.Select(i => (i.Fine!.AssessmentId, i.ConfirmedAmount)));
    }

    [Fact]
    public void Penalties_that_cannot_be_confirmed_record_nothing()
    {
        var cut = Render(PaymentStatus.Unpaid, 330m, quoteRefuses: true);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Contains("Total amount due", Sheet(cut, "Record Monthly Payment").TextContent));

        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("Nothing was recorded", Sheet(cut, "Record Monthly Payment").TextContent));
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
        Assert.Empty(_queue);
    }

    [Fact]
    public void Paid_rent_with_open_penalties_is_not_paid_and_collects_them_without_touching_the_rent()
    {
        var cut = Render(PaymentStatus.Paid, 330m);
        cut.WaitForAssertion(() => Assert.Contains("Penalties due", cut.Find(".tx-card .tx-badge").TextContent));
        Assert.Equal("Collect", cut.Find(".tx-card .tx-action").TextContent.Trim());
        Assert.Contains("0 of 1", cut.Find(".npm-context").TextContent);

        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Contains("Total penalties", Sheet(cut, "Collect Penalties").TextContent));
        Sheet(cut, "Collect Penalties").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Collect ₱330").Click();

        cut.WaitForAssertion(() => Assert.Single(_queue));
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
        Assert.Equal([300m, 30m], _queue[0].CollectionSession!.Intent.Items.Select(i => i.ConfirmedAmount));
        Assert.Null(_queue[0].CollectionSession!.Intent.ReceiptReference);
    }

    [Fact]
    public void Account_without_penalties_records_the_rent_exactly_as_before()
    {
        var cut = Render(PaymentStatus.Unpaid, 0m);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Sheet(cut, "Record Monthly Payment"));
        Assert.Empty(Sheet(cut, "Record Monthly Payment").QuerySelectorAll("dl[aria-label='Penalties']"));

        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Equal(["rent"], _calls));
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(It.IsAny<CollectionSourceIdentity?>()), Times.Never);
        _api.Verify(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>()), Times.Never);
        Assert.Empty(_queue);
    }
}
