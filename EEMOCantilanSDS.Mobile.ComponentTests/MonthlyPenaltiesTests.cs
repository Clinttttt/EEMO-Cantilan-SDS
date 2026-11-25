using Bunit;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayAhead;
using EEMOCantilanSDS.Application.Dtos.Payments;
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
    private readonly Guid _stall = Guid.NewGuid(), _contract = Guid.NewGuid(), _surcharge = Guid.NewGuid(), _interest = Guid.NewGuid(), _interestNov = Guid.NewGuid();
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly List<PendingOperation> _queue = [];
    private readonly List<string> _calls = [];

    private IRenderedComponent<MonthlyCollection> Render(PaymentStatus status, decimal penaltiesDue, bool quoteRefuses = false,
        IReadOnlyList<MobileUnpaidMonthDto>? earlier = null, Guid? openStall = null)
    {
        var paid = status == PaymentStatus.Paid ? 1200m : 0m;
        var row = new MobileMonthlyStallCollectionDto(_stall, "2", "Ruel Ramirez", "Ruel Ramirez", "", 1200m, status, paid, 1200m - paid,
            null, status != PaymentStatus.Unpaid, ContractId: _contract, PenaltiesDue: penaltiesDue);
        _api.Setup(x => x.GetMonthlyCollectionAsync(FacilityCode.NCC, 2026, 11)).ReturnsAsync(Result<MobileMonthlyCollectionDto>.Success(
            new(FacilityCode.NCC, "Scratch Center", 2026, 11, Today, 1, status == PaymentStatus.Paid ? 1 : 0, 0,
                status == PaymentStatus.Unpaid ? 1 : 0, paid, 1200m - paid, [row])));
        _api.Setup(x => x.GetUnpaidMonthsAsync(_stall, null)).ReturnsAsync(Result<IReadOnlyList<MobileUnpaidMonthDto>>.Success(earlier ?? []));

        CollectionSessionSourceChoice Fine(Guid id, string name, string month, decimal amount) => new($"FINE|{id:N}", CollectionSessionItemKind.FineAssessment,
            "PENALTIES", name, month, new(FineAssessmentId: id, FineVersion: 1), RevenueInstrumentType.OfficialReceipt,
            CollectionSessionAmountRule.PreparedBalance, ServerAmount: amount, MaximumAmount: amount);
        // Listed as the server orders them (by when they were assessed); the statement puts the surcharge first.
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(It.Is<CollectionSourceIdentity?>(i => i!.Kind == SourceIdentityKind.Occupancy && i.Id == _contract)))
            .Callback(() => _calls.Add("discover"))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
                [new(CollectionSessionItemKind.FineAssessment, "PENALTIES", "Fines / Penalties", true, true, null, null, false, [], true,
                    [Fine(_interest, "Interest", "October 2026", 20m), Fine(_surcharge, "Surcharge", "October 2026", 300m), Fine(_interestNov, "Interest", "November 2026", 10m)])])));
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
        Services.AddSingleton(new EEMOCantilanSDS.Mobile.Collections.ArrearsWorkspaceHolder());
        JSInterop.Mode = JSRuntimeMode.Loose;
        if (openStall is { } stall) Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/collect/ncc?stall={stall}");
        return RenderComponent<MonthlyCollection>(p => p.Add(x => x.Facility, "ncc"));
    }

    private static AngleSharp.Dom.IElement Sheet(IRenderedComponent<MonthlyCollection> cut, string title) =>
        cut.FindAll(".sheet").Single(x => x.QuerySelector(".sheet-title")?.TextContent.Trim() == title);

    // Each statement line as (name, the months it is for, amount).
    private static List<(string Name, string? Months, string Amount)> Statement(AngleSharp.Dom.IElement sheet) =>
        sheet.QuerySelectorAll("dl[aria-label='Amount due'] .money-row").Select(r =>
        {
            var dt = r.QuerySelector("dt")!;
            var months = dt.QuerySelector("small")?.TextContent.Trim();
            var name = months is null ? dt.TextContent : dt.TextContent.Replace(months, "");
            return (name.Trim(), months, r.QuerySelector("dd")!.TextContent.Trim());
        }).ToList();

    [Fact]
    public void Unpaid_rent_with_penalties_shows_the_statement_total_and_records_both_on_one_receipt()
    {
        var cut = Render(PaymentStatus.Unpaid, 330m);
        cut.WaitForAssertion(() => Assert.Contains("Penalties ₱330", cut.Find(".tx-card").TextContent));

        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Contains("Total amount due", Sheet(cut, "Record Monthly Payment").TextContent));
        var sheet = Sheet(cut, "Record Monthly Payment");
        Assert.Equal([("November 2026 rental", null, "₱1,200"), ("Surcharge", "October 2026", "₱300"),
            ("Interest", "October – November 2026 · 2 months", "₱30"), ("Total amount due", null, "₱1,530")], Statement(sheet));
        Assert.Contains("₱1,530", sheet.QuerySelector(".status-option-sub")!.TextContent);
        Assert.DoesNotContain("receipt", sheet.QuerySelector(".status-option-sub")!.TextContent);

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
        Assert.Equal([(_interest, 20m), (_surcharge, 300m), (_interestNov, 10m)], checkout.Intent.Items.Select(i => (i.Fine!.AssessmentId, i.ConfirmedAmount)));
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
        cut.WaitForAssertion(() => Assert.Contains("Total amount due", Sheet(cut, "Collect Penalties").TextContent));
        Assert.Equal([("November 2026 rental", "₱1,200 of ₱1,200 paid", "Paid"), ("Surcharge", "October 2026", "₱300"),
            ("Interest", "October – November 2026 · 2 months", "₱30"), ("Total amount due", null, "₱330")], Statement(Sheet(cut, "Collect Penalties")));
        Sheet(cut, "Collect Penalties").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Collect ₱330").Click();

        cut.WaitForAssertion(() => Assert.Single(_queue));
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
        Assert.Equal([20m, 300m, 10m], _queue[0].CollectionSession!.Intent.Items.Select(i => i.ConfirmedAmount));
        Assert.Null(_queue[0].CollectionSession!.Intent.ReceiptReference);
    }

    private IRenderedComponent<MonthlyCollection> OpenWithCarried(decimal penaltiesDue = 330m)
    {
        var cut = Render(PaymentStatus.Unpaid, penaltiesDue, earlier: [new(2025, 12, 1200m, 0m, 1200m), new(2026, 10, 1200m, 0m, 1200m)]);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Contains("Carried over", Sheet(cut, "Record Monthly Payment").TextContent));
        return cut;
    }

    [Fact]
    public void Carried_over_months_are_stated_on_the_sheet_each_with_its_own_penalties_and_counted_once_in_the_total()
    {
        var cut = OpenWithCarried();
        var sheet = Sheet(cut, "Record Monthly Payment");
        Assert.Null(sheet.QuerySelector(".earlier-card"));                                              // nothing to navigate to
        Assert.Equal(["Arrears · 2025", "Carried over", "This month"], sheet.QuerySelectorAll(".money-head").Select(h => h.TextContent.Trim()));
        cut.WaitForAssertion(() => Assert.Equal([
            ("December 2025 rental", null, "₱1,200"),
            ("October 2026 rental", null, "₱1,200"), ("Surcharge", null, "₱300"), ("Interest", null, "₱20"),
            ("November 2026 rental", null, "₱1,200"), ("Interest", null, "₱10"),
            ("Total amount due", null, "₱3,930")], Statement(Sheet(cut, "Record Monthly Payment"))));
        Assert.Equal("₱3,930", Sheet(cut, "Record Monthly Payment").QuerySelector(".status-option-sub")!.TextContent.Trim());
    }

    [Fact]
    public void Paid_in_full_records_the_carried_months_oldest_first_then_this_month_then_every_penalty()
    {
        var cut = OpenWithCarried();
        cut.WaitForAssertion(() => Assert.Contains("₱3,930", Sheet(cut, "Record Monthly Payment").TextContent));
        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("queue", _calls));
        Assert.Equal(["discover", "quote", "rent", "rent", "rent", "queue"], _calls);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r =>
            r.BillingYear == 2025 && r.BillingMonth == 12 && r.Status == PaymentStatus.Paid && r.PartialAmount == null)), Times.Once);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r =>
            r.BillingYear == 2026 && r.BillingMonth == 10 && r.Status == PaymentStatus.Paid)), Times.Once);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r =>
            r.BillingMonth == null && r.Status == PaymentStatus.Paid)), Times.Once);                     // this month, as before
        Assert.Equal(330m, _queue.Single().CollectionSession!.Intent.Items.Sum(i => i.ConfirmedAmount));
    }

    [Fact]
    public void A_refused_carried_month_stops_there_and_records_neither_this_month_nor_the_penalties()
    {
        var cut = OpenWithCarried();
        cut.WaitForAssertion(() => Assert.Contains("₱3,930", Sheet(cut, "Record Monthly Payment").TextContent));
        _api.Setup(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r => r.BillingMonth == 10)))
            .Callback(() => _calls.Add("refused")).ReturnsAsync(Result<bool>.Failure("October is locked."));
        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("Recorded 1 of 2 earlier months; November 2026 and the penalties were not. October is locked.",
            Sheet(cut, "Record Monthly Payment").TextContent));
        Assert.Equal(["discover", "quote", "rent", "refused"], _calls);
        Assert.Empty(_queue);
    }

    [Fact]
    public void A_part_payment_clears_the_oldest_month_first_and_takes_no_penalties()
    {
        var cut = OpenWithCarried();
        cut.WaitForAssertion(() => Assert.Contains("₱3,930", Sheet(cut, "Record Monthly Payment").TextContent));
        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Contains("Partial Payment")).Click();
        Sheet(cut, "Record Monthly Payment").QuerySelector("input[aria-label='Amount collected']")!.Input("1500");
        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Equal(["discover", "rent", "rent"], _calls));
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r =>
            r.BillingYear == 2025 && r.BillingMonth == 12 && r.Status == PaymentStatus.Paid)), Times.Once);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.Is<RecordMobileMonthlyCollectionRequest>(r =>
            r.BillingMonth == 10 && r.Status == PaymentStatus.Partial && r.PartialAmount == 300m)), Times.Once);
        _api.Verify(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>()), Times.Never);
        Assert.Empty(_queue);
    }

    [Fact]
    public void Many_carried_months_fold_into_one_line_until_tapped()
    {
        var cut = Render(PaymentStatus.Unpaid, 0m, earlier: [.. Enumerable.Range(6, 5).Select(m => new MobileUnpaidMonthDto(2026, m, 1200m, 0m, 1200m))]);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Contains("June – October 2026 · 5 months", Sheet(cut, "Record Monthly Payment").QuerySelector(".money-fold")!.TextContent));
        Assert.Contains("₱6,000", Sheet(cut, "Record Monthly Payment").QuerySelector(".money-fold")!.TextContent);
        Assert.Equal(("Total amount due", null, "₱7,200"), Statement(Sheet(cut, "Record Monthly Payment")).Last());

        Sheet(cut, "Record Monthly Payment").QuerySelector(".money-fold")!.Click();
        Assert.Equal(6, Statement(Sheet(cut, "Record Monthly Payment")).Count(r => r.Name.EndsWith("rental")));
    }

    [Fact]
    public void Opened_for_one_stall_goes_straight_to_its_sheet()
    {
        var cut = Render(PaymentStatus.Unpaid, 330m, openStall: _stall);
        cut.WaitForAssertion(() => Assert.Equal("Total amount due", Statement(Sheet(cut, "Record Monthly Payment")).Last().Name));
    }

    [Fact]
    public void Account_without_penalties_records_the_rent_exactly_as_before()
    {
        var cut = Render(PaymentStatus.Unpaid, 0m);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Sheet(cut, "Record Monthly Payment"));
        Assert.Equal([("November 2026 rental", null, "₱1,200"), ("Total amount due", null, "₱1,200")], Statement(Sheet(cut, "Record Monthly Payment")));

        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Equal(["rent"], _calls));
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(It.IsAny<CollectionSourceIdentity?>()), Times.Never);
        _api.Verify(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>()), Times.Never);
        Assert.Empty(_queue);
    }

    // ── Pay ahead (IA-071): the office's own months, counts and total, recorded whole by the office or not at all ──
    private void Ahead(int fromMonth, IReadOnlyList<int> counts, string? heldBy = null, Result<bool>? outcome = null)
    {
        var first = new DateOnly(2026, fromMonth, 1);
        IReadOnlyList<PayAheadMonthDto> months = heldBy is not null ? [] : Enumerable.Range(0, counts.Max())
            .Select(i => new PayAheadMonthDto(first.AddMonths(i).Year, first.AddMonths(i).Month, 1200m)).ToList();
        _api.Setup(x => x.GetPayAheadAsync(_stall)).ReturnsAsync(Result<PayAheadOptionsDto>.Success(new(months, heldBy is not null ? [] : counts, heldBy)));
        _api.Setup(x => x.RecordPayAheadAsync(It.IsAny<RecordMobilePayAheadCommand>())).Callback(() => _calls.Add("ahead"))
            .ReturnsAsync(outcome ?? Result<bool>.Success(true));
    }

    private static AngleSharp.Dom.IElement AheadOption(AngleSharp.Dom.IElement sheet) =>
        sheet.QuerySelectorAll("button.status-option").Single(b => b.QuerySelector(".status-option-label")?.TextContent.Trim() == "Pay ahead");

    private static List<string> AheadRows(AngleSharp.Dom.IElement sheet) =>
        sheet.QuerySelectorAll("dl[aria-label='Months paid ahead'] .money-row").Select(r => r.QuerySelector("dt")!.TextContent.Trim() + " " + r.QuerySelector("dd")!.TextContent.Trim()).ToList();

    [Fact]
    public void An_unpaid_month_is_paid_ahead_with_the_months_after_it_and_never_queued()
    {
        Ahead(11, [1, 2, 3, 4, 12]);
        var cut = Render(PaymentStatus.Unpaid, 0m);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => AheadOption(Sheet(cut, "Record Monthly Payment")));

        AheadOption(Sheet(cut, "Record Monthly Payment")).Click();
        var sheet = Sheet(cut, "Record Monthly Payment");
        // One month on its own is Paid in Full, so the choices start at two; the year is the annual permission.
        Assert.Equal(["2 months", "3 months", "4 months", "12 months"], sheet.QuerySelectorAll(".ahead-count").Select(b => b.TextContent.Trim()));
        sheet.QuerySelectorAll(".ahead-count").Single(b => b.TextContent.Trim() == "3 months").Click();
        Assert.Equal(["November 2026 rental ₱1,200", "December 2026 rental ₱1,200", "January 2027 rental ₱1,200", "Total ₱3,600"], AheadRows(Sheet(cut, "Record Monthly Payment")));
        // The ahead block carries its own receipt number; the optional receipt entry is not shown beside it.
        Assert.Single(Sheet(cut, "Record Monthly Payment").QuerySelectorAll("input[placeholder='000001']"));

        Sheet(cut, "Record Monthly Payment").QuerySelector("input[placeholder='000001']")!.Input("777");
        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("ahead", _calls));
        _api.Verify(x => x.RecordPayAheadAsync(new RecordMobilePayAheadCommand(_stall, 2026, 11, 3, 3600m, "777")), Times.Once);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
        Assert.Empty(_queue);
    }

    [Fact]
    public void An_older_open_month_holds_pay_ahead_back_with_the_offices_reason()
    {
        Ahead(11, [1], heldBy: "Pay October 2026 first.");
        var cut = Render(PaymentStatus.Unpaid, 0m);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => AheadOption(Sheet(cut, "Record Monthly Payment")));

        var option = AheadOption(Sheet(cut, "Record Monthly Payment"));
        Assert.True(option.HasAttribute("disabled"));
        Assert.Contains("Pay October 2026 first.", option.TextContent);
    }

    [Fact]
    public void A_refused_pay_ahead_records_nothing_and_says_so()
    {
        Ahead(11, [1, 2], outcome: Result<bool>.Failure("This receipt number is already used."));
        var cut = Render(PaymentStatus.Unpaid, 0m);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => AheadOption(Sheet(cut, "Record Monthly Payment")));
        AheadOption(Sheet(cut, "Record Monthly Payment")).Click();
        Sheet(cut, "Record Monthly Payment").QuerySelector("input[placeholder='000001']")!.Input("777");

        Sheet(cut, "Record Monthly Payment").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("already used. Nothing was recorded.", Sheet(cut, "Record Monthly Payment").TextContent));
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
        Assert.Empty(_queue);
    }

    [Fact]
    public void A_paid_month_pays_the_months_after_it_from_its_receipt()
    {
        Ahead(12, [1, 2, 3]);
        var cut = Render(PaymentStatus.Paid, 0m);
        cut.WaitForAssertion(() => cut.Find(".tx-card"));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Pay ahead · from December 2026"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Pay ahead · from December 2026").Click();
        Assert.Equal(["1 month", "2 months", "3 months"], Sheet(cut, "Pay Ahead").QuerySelectorAll(".ahead-count").Select(b => b.TextContent.Trim()));
        Assert.Equal(["December 2026 rental ₱1,200", "Total ₱1,200"], AheadRows(Sheet(cut, "Pay Ahead")));

        // The receipt number is required before anything goes to the office.
        Sheet(cut, "Pay Ahead").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enter the official receipt number.", Sheet(cut, "Pay Ahead").TextContent));
        _api.Verify(x => x.RecordPayAheadAsync(It.IsAny<RecordMobilePayAheadCommand>()), Times.Never);

        Sheet(cut, "Pay Ahead").QuerySelector("input[placeholder='000001']")!.Input("778");
        Sheet(cut, "Pay Ahead").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("ahead", _calls));
        _api.Verify(x => x.RecordPayAheadAsync(new RecordMobilePayAheadCommand(_stall, 2026, 12, 1, 1200m, "778")), Times.Once);
        _api.Verify(x => x.RecordMonthlyCollectionAsync(It.IsAny<RecordMobileMonthlyCollectionRequest>()), Times.Never);
    }
}
