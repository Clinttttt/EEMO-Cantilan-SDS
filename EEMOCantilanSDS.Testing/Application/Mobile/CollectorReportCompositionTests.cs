using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Mobile;

/// <summary>
/// The collector's Mobile report states every authoritative collection they took, legacy and canonical, exactly once. A
/// posted Landing/Berthing Cash Ticket is part of By Month, Summary and the classification breakdown; a walk-up ticket is
/// never turned into a payee; and the canonical side is selected by its own business date over the whole month.
/// </summary>
public class CollectorReportCompositionTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly Guid Landing = Guid.NewGuid();
    private static readonly Guid Market = Guid.NewGuid();

    private static MobileCollectorReportDto Legacy(bool daily = false, params MobileReportTransactionDto[] transactions) => new(
        2026, 10, Oct1, new DateOnly(2026, 10, 31), daily,
        new MobileReportTotalsDto(transactions.Sum(t => t.Amount), 0m, transactions.Length, 0, 0, 0, 0, 0, 0),
        [],
        transactions.Length == 0 ? [] : [new MobileReportPeriodSummaryDto(Oct1, transactions.Sum(t => t.Amount), transactions.Length, 1, 0, 0, 0)],
        [], transactions, []);

    private static CollectorCollectionFactDto Fact(
        string ct, decimal amount, Guid classification, string name, DateOnly? day = null, Guid? payorId = null, string? payorName = null) =>
        new(Guid.NewGuid(), day ?? Oct1, ct, RevenueInstrumentType.CashTicket, payorId, payorName, amount,
            [new RemittanceBreakdownDto(classification, name, amount)]);

    private static MobileReportTransactionDto Stall(decimal amount) =>
        new(FacilityCode.TCC, "Tampak Commercial Center", "A-1", "Maria", Oct1, amount, false, "OR-1");

    [Fact]
    public void A_OneLandingCashTicket_IsTheMonthsTotal_AndItsOnlyTransaction()
    {
        var report = CollectorReportComposer.Compose(Legacy(), [Fact("CT000001", 100m, Landing, "Landing/Berthing")]);

        Assert.Equal(100m, report.Totals.CollectedAmount);
        Assert.Equal(1, report.Totals.TransactionCount);
        var month = Assert.Single(report.Periods);
        Assert.Equal((Oct1, 100m, 1), (month.PeriodDate, month.CollectedAmount, month.TransactionCount));
        var line = Assert.Single(report.Breakdown!);
        Assert.Equal(("Landing/Berthing", true, 100m, 1), (line.Label, line.IsClassification, line.Amount, line.TransactionCount));
    }

    [Fact]
    public void B_TheSameCollectionReadTwice_IsNotAddedTwice_TheFactsAreTheOnlyCanonicalSource()
    {
        var fact = Fact("CT000001", 100m, Landing, "Landing/Berthing");
        var report = CollectorReportComposer.Compose(Legacy(), [fact]);
        // Composition is not cumulative: the legacy part is the reader's, the canonical part is replaced, never re-added.
        Assert.Equal(100m, report.Totals.CollectedAmount);
        Assert.Equal(100m, report.Totals.CanonicalCollectedAmount);
    }

    [Fact]
    public void C_LegacyAndCanonical_DifferentMoney_AreBothCounted_AndStatedOnTheirOwnBasis()
    {
        var report = CollectorReportComposer.Compose(Legacy(false, Stall(500m)), [Fact("CT000001", 100m, Landing, "Landing/Berthing")]);

        Assert.Equal(600m, report.Totals.CollectedAmount);
        Assert.Equal(2, report.Totals.TransactionCount);
        Assert.Equal(600m, Assert.Single(report.Periods).CollectedAmount);
        Assert.Contains(report.Breakdown!, b => b is { Label: "Tampak Commercial Center", IsClassification: false, Amount: 500m });
        Assert.Contains(report.Breakdown!, b => b is { Label: "Landing/Berthing", IsClassification: true, Amount: 100m });
    }

    [Fact]
    public void E_MarketFeesAndLanding_AddUp_AndAreBrokenDownByClassification()
    {
        var report = CollectorReportComposer.Compose(Legacy(),
        [
            Fact("CT000001", 30m, Market, "Market Fees"),
            Fact("CT000002", 100m, Landing, "Landing/Berthing")
        ]);

        Assert.Equal(130m, report.Totals.CollectedAmount);
        Assert.Equal(2, report.Totals.TransactionCount);
        Assert.Equal(["Landing/Berthing", "Market Fees"], report.Breakdown!.Select(b => b.Label));
        Assert.Equal([100m, 30m], report.Breakdown!.Select(b => b.Amount));
    }

    [Fact]
    public void F_AWalkUpTicket_CountsInTheTotal_ButIsNeverMadeIntoAPayee()
    {
        var report = CollectorReportComposer.Compose(Legacy(), [Fact("CT000001", 100m, Landing, "Landing/Berthing")]);

        Assert.Equal(100m, report.Totals.CollectedAmount);
        Assert.Equal(0, report.Totals.PayeeCount);
        Assert.Empty(report.Payees);
        Assert.Empty(report.NamedPayors!);
        Assert.Equal((100m, 1), (report.Totals.UnnamedCollectedAmount, report.Totals.UnnamedTransactionCount));
        Assert.Equal(0, Assert.Single(report.Periods).PayeeCount);
    }

    [Fact]
    public void ALinkedPayor_IsNamedFromTheLink_AndCountedOnce()
    {
        var payor = Guid.NewGuid();
        var report = CollectorReportComposer.Compose(Legacy(),
        [
            Fact("CT000001", 100m, Landing, "Landing/Berthing", payorId: payor, payorName: "Cantilan Fishers Assn."),
            Fact("CT000002", 100m, Landing, "Landing/Berthing", payorId: payor, payorName: "Cantilan Fishers Assn.")
        ]);

        var named = Assert.Single(report.NamedPayors!);
        Assert.Equal(("Cantilan Fishers Assn.", 200m, 2), (named.PayorName, named.Amount, named.TransactionCount));
        Assert.Equal(1, report.Totals.PayeeCount);
        Assert.Equal(0, report.Totals.UnnamedTransactionCount);
    }

    [Fact]
    public void AVoidedCollection_ContributesNothing_AndIsNotATransaction()
    {
        var report = CollectorReportComposer.Compose(Legacy(), [Fact("CT000001", 0m, Landing, "Landing/Berthing")]);

        Assert.Equal(0m, report.Totals.CollectedAmount);
        Assert.Equal(0, report.Totals.TransactionCount);
        Assert.Empty(report.Periods);
        Assert.Empty(report.Breakdown!);
    }

    [Fact]
    public void InDailyMode_ACanonicalCollection_JoinsItsOwnBusinessDay()
    {
        var oct2 = new DateOnly(2026, 10, 2);
        var report = CollectorReportComposer.Compose(Legacy(daily: true, Stall(50m)),
            [Fact("CT000001", 100m, Landing, "Landing/Berthing", day: Oct1), Fact("CT000002", 30m, Market, "Market Fees", day: oct2)]);

        Assert.Equal([oct2, Oct1], report.Periods.Select(p => p.PeriodDate));
        Assert.Equal(150m, report.Periods.Single(p => p.PeriodDate == Oct1).CollectedAmount);
        Assert.Equal(30m, report.Periods.Single(p => p.PeriodDate == oct2).CollectedAmount);
    }

    // ── The handler: one read of the canonical facts, over the selected month's business dates, inclusive ──

    private static (GetCollectorReportQueryHandler Handler, Mock<ICollectorCollectionFacts> Facts) Handler(
        Result<IReadOnlyList<CollectorCollectionFactDto>> facts, DateOnly today)
    {
        var collector = CollectorUser.Create("Ana", "C-1", "ana", null, null, new HashedPassword("h"));
        var repository = new Mock<ICollectorRepository>();
        repository.Setup(x => x.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        var mobile = new Mock<ICollectorMobileQueries>();
        mobile.Setup(x => x.GetCollectorReportAsync(collector.Id, It.IsAny<IReadOnlyCollection<FacilityCode>>(),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Legacy());
        var reader = new Mock<ICollectorCollectionFacts>();
        reader.Setup(x => x.GetMyCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.CollectorId).Returns(collector.Id);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.PhilippineToday).Returns(today);
        return (new GetCollectorReportQueryHandler(repository.Object, mobile.Object, reader.Object, user.Object, clock.Object), reader);
    }

    [Theory]
    [InlineData(2026, 10, 1)]
    [InlineData(2026, 10, 15)]
    [InlineData(2026, 11, 3)]
    public async Task TheCanonicalSideReadsTheWholeSelectedMonth_ByBusinessDate_Inclusive_WhateverTodayIs(int y, int m, int d)
    {
        IReadOnlyList<CollectorCollectionFactDto> facts = [Fact("CT000001", 100m, Landing, "Landing/Berthing")];
        var (handler, reader) = Handler(Result<IReadOnlyList<CollectorCollectionFactDto>>.Success(facts), new DateOnly(y, m, d));

        var result = await handler.Handle(new GetCollectorReportQuery(null, 2026, 10), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value!.Totals.CollectedAmount);
        // Oct 1 and Oct 31 are inside; Sep 30 and Nov 1 are outside. No UTC instant is involved in the selection.
        reader.Verify(x => x.GetMyCollectionsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailedCanonicalRead_FailsTheReport_RatherThanStatingNought()
    {
        var (handler, _) = Handler(Result<IReadOnlyList<CollectorCollectionFactDto>>.Forbidden(), Oct1);

        var result = await handler.Handle(new GetCollectorReportQuery(null, 2026, 10), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
