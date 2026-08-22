using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The financial report composes the canonical per-facility report (stall facilities) with the
/// paid-on-service facilities (SLH/TRM/TPM). These tests lock in: totals reconcile to the facility
/// breakdown; the rate is amount-based; delinquent (3+ months) and arrears (1–2 months) are split;
/// paid-on-service facilities carry no unpaid balance and a 100% rate; recent records are mapped.
/// </summary>
public class GetFinancialReportQueryHandlerTests
{
    private static StallComplianceDto Payor(string stallNo, string occupant, decimal paid, decimal balance, int missedMonths) =>
        new(Guid.NewGuid(), stallNo, occupant, occupant, "", "", 0m, 0m,
            balance > 0 ? "Partial" : "Paid", paid, balance, null, missedMonths, 0, null, 0, paid + balance);

    private static ClosedStallAccountDto Closed(string stallNo, decimal uncollected,
        InactiveAccountState state = InactiveAccountState.Superseded) =>
        new(Guid.NewGuid(), state, FacilityCode.NPM, "New Public Market", stallNo,
            "Former Occupant", "Former Occupant", new DateOnly(2023, 6, 7), 3, 900m, null,
            new DateOnly(2026, 6, 7), 0m, uncollected, null);

    private static FacilityReportsDto Report(decimal collected, decimal outstanding, decimal rate, int paid, int partial, int unpaid, IReadOnlyList<StallComplianceDto> compliance, FeeTypeBreakdownDto? feeBreakdown = null) =>
        new(
            TotalRevenue: collected,
            RevenueGrowthPercentage: 0m,
            CollectionRate: rate,
            CollectionGrowthPercentage: 0m,
            OccupiedStalls: compliance.Count,
            TotalStalls: compliance.Count,
            PendingPaymentCount: compliance.Count(c => c.Balance > 0m),
            PendingPaymentAmount: outstanding,
            RevenueTrend: new[] { new RevenueTrendDto("Mar", collected, collected + outstanding, true) },
            PaymentDistribution: new PaymentStatusDistributionDto(paid, 0m, partial, 0m, unpaid, 0m),
            SectionBreakdown: Array.Empty<SectionBreakdownDto>(),
            TopStalls: Array.Empty<TopStallDto>(),
            CollectionPerformance: new CollectionPerformanceDto(paid, partial, unpaid),
            DailyCollectionStreak: null,
            FeeTypeBreakdown: feeBreakdown,
            FishKiloTrend: Array.Empty<FishKiloTrendDto>(),
            StallCompliance: compliance);

    /// <summary>The stall-repository stub the last <see cref="Build"/> wired in, so a test can restate its rows.
    /// xUnit does not run tests of one class in parallel, so a single slot is safe here.</summary>
    private static Mock<IClosedStallAccountQueries>? _lastStalls;

    private static (GetFinancialReportQueryHandler handler, Mock<IFacilityReportsRepository> reports) Build()
    {
        var reports = new Mock<IFacilityReportsRepository>();
        var empty = Report(0m, 0m, 0m, 0, 0, 0, Array.Empty<StallComplianceDto>());

        // NPM: collected 80,000 / outstanding 20,000 (rate 80). Three occupied stalls:
        //   one delinquent (3 missed months), one in arrears (1 missed month), one fully paid.
        //   Fee breakdown: ₱810 daily-fee + ₱346 fish (346 kg @ ₱1/kg), and the counted records behind them —
        //   27 collections recorded of 30 collectable stall-days. Counted by the repository at each stall's own
        //   daily fee rather than inferred here by dividing money by one rate.
        var npm = Report(80_000m, 20_000m, 80m, paid: 6, partial: 2, unpaid: 0, new[]
        {
            Payor("12", "Rosa Magbanua", 0m, 12_000m, 3),   // delinquent
            Payor("07", "Maria Velasco", 500m, 3_000m, 1),  // arrears
            Payor("01", "Pedro Santos", 900m, 0m, 0),       // fully paid (occupied, no balance)
        }, feeBreakdown: new FeeTypeBreakdownDto(810m, 346m, null, PaidDayRecords: 27, ExpectedDayRecords: 30));

        reports.Setup(r => r.GetFacilityReportsAsync(
                It.IsAny<FacilityCode>(), It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FacilityCode code, ReportPeriod _, int _, int? _, int? _, CancellationToken _) =>
                code == FacilityCode.NPM ? npm : empty);

        // Delinquency comes from the shared rolling-window method: one delinquent (3 mo) + one arrears (1 mo).
        reports.Setup(r => r.GetDelinquentStallsAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DelinquentStallDto>
            {
                new(FacilityCode.TCC, "04", "Rosa Magbanua", 3, 12_000m),
                new(FacilityCode.NPM, "22", "Maria Velasco", 1, 3_000m),
            });

        var slaughter = new Mock<ISlaughterRepository>();
        slaughter.Setup(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlaughterTransactionDto>());

        // TRM (paid on service): two trips → collected 60, 2 records.
        var trm = new Mock<ITrmRepository>();
        trm.Setup(t => t.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 1, DriverName = "Diego", Route = "A", Fee = 30m, ORNumber = "OR-1", RecordedAt = new DateTime(2026, 3, 2) },
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 2, DriverName = "Diego", Route = "A", Fee = 30m, ORNumber = "OR-2", RecordedAt = new DateTime(2026, 3, 9) },
            });

        var tpm = new Mock<ITpmRepository>();
        tpm.Setup(t => t.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TpmVendorAttendanceDto>());

        var feed = new Mock<ITransactionFeedRepository>();
        feed.Setup(f => f.GetRecentTransactionsAsync(It.IsAny<FacilityCode?>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TransactionFeedDto(Guid.NewGuid(), FacilityCode.NPM, "New Public Market", new DateTime(2026, 3, 25), true, "Luz Cano", "5", "Daily Fee", 930m, "OR-9", "Paid", "Admin")
            });

        // Tenant operates all eight facilities (matches the always-8 expectation these tests lock in).
        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(f => f.GetFacilityNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<FacilityCode, string>)Enum.GetValues<FacilityCode>().Where(c => (int)c < 100).ToDictionary(c => c, c => c.ToString()));

        // Two ended NPM accounts (handed over), each with a ₱32,910 historical uncollected balance.
        var stalls = new Mock<IClosedStallAccountQueries>();
        stalls.Setup(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Closed("91", 32_910m), Closed("92", 32_910m) });
        _lastStalls = stalls;

        var handler = new GetFinancialReportQueryHandler(
            reports.Object,
            slaughter.Object,
            trm.Object,
            tpm.Object,
            feed.Object,
            facilities.Object,
            stalls.Object,
            CacheTestDoubles.FeeRateResolver,
            CacheTestDoubles.PassthroughCache,
            CacheTestDoubles.Tenant,
            new EemoCacheOptions(),
            new FixedClock(DateTime.UtcNow));
        return (handler, reports);
    }

    [Fact]
    public async Task TheFollowUpTotalsCountEveryAccount_EvenWhenTheListsAreCapped()
    {
        // The report lists at most 50 accounts per column so the payload stays bounded. The header, though, says
        // "N accounts need follow-up · ₱X outstanding in full" — and it used to count and sum the CAPPED lists, so an
        // office with more accounts than the cap was told it had fewer and was owed less, on a printed report that
        // claimed to be complete. The totals must describe every account; only the lists are shortened.
        var (handler, reports) = Build();

        // 60 delinquent (3+ unpaid months) and 60 in arrears (1–2) — both past the cap of 50.
        var many = new List<DelinquentStallDto>();
        for (var i = 0; i < 60; i++)
            many.Add(new(FacilityCode.NPM, $"D{i:00}", $"Delinquent {i:00}", 3, 1_000m));
        for (var i = 0; i < 60; i++)
            many.Add(new(FacilityCode.NPM, $"A{i:00}", $"Arrears {i:00}", 1, 100m));

        reports.Setup(r => r.GetDelinquentStallsAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(many);

        var result = await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        // The lists are capped …
        Assert.Equal(50, r.Delinquent.Count);
        Assert.Equal(50, r.Arrears.Count);

        // … and the totals are not.
        Assert.Equal(60, r.DelinquentAccountsTotal);
        Assert.Equal(60_000m, r.DelinquentOutstandingTotal);   // 60 × ₱1,000
        Assert.Equal(60, r.ArrearsAccountsTotal);
        Assert.Equal(6_000m, r.ArrearsOutstandingTotal);       // 60 × ₱100

        // Stated as the office reads it: what the header would show is the whole debt, not the visible part.
        Assert.Equal(120, r.DelinquentAccountsTotal + r.ArrearsAccountsTotal);
        Assert.Equal(66_000m, r.DelinquentOutstandingTotal + r.ArrearsOutstandingTotal);
        Assert.NotEqual(r.Delinquent.Sum(d => d.Balance) + r.Arrears.Sum(a => a.Balance),
                        r.DelinquentOutstandingTotal + r.ArrearsOutstandingTotal);
    }

    [Fact]
    public async Task WithFewerAccountsThanTheCap_TheTotalsAndTheListsAgree()
    {
        // The ordinary case, and the one that let the old bug hide: below the cap the two ways of counting give the same
        // answer, so nothing looked wrong until an office grew past fifty accounts in one bucket.
        var (handler, _) = Build();   // the default fixture has one delinquent and one in arrears

        var result = await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None);

        var r = result.Value!;

        Assert.Equal(r.Delinquent.Count, r.DelinquentAccountsTotal);
        Assert.Equal(r.Arrears.Count, r.ArrearsAccountsTotal);
        Assert.Equal(r.Delinquent.Sum(d => d.Balance), r.DelinquentOutstandingTotal);
        Assert.Equal(r.Arrears.Sum(a => a.Balance), r.ArrearsOutstandingTotal);
    }

    [Fact]
    public async Task TheAllTimeViewCarriesTheFollowUpTotalsToo()
    {
        // "All time" builds its DTO from the current year's. The totals have to travel with the lists they describe, or the
        // header reads nought accounts and no money owed above two populated columns.
        var (handler, _) = Build();

        var result = await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, 2026, null, null, AllTime: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        Assert.Equal(r.Delinquent.Count, r.DelinquentAccountsTotal);
        Assert.Equal(r.Arrears.Count, r.ArrearsAccountsTotal);
        Assert.Equal(r.Delinquent.Sum(d => d.Balance), r.DelinquentOutstandingTotal);
        Assert.Equal(r.Arrears.Sum(a => a.Balance), r.ArrearsOutstandingTotal);
    }

    [Fact]
    public async Task AllFacilities_TotalsReconcile_RateIsAmountBased()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        // Collected = NPM 80,000 + TRM 60 ; unpaid = NPM 20,000 (TRM has none).
        Assert.Equal(80_060m, r.Collected);
        Assert.Equal(20_000m, r.CurrentPeriodUnpaid);
        Assert.Equal(100_060m, r.Billed);
        Assert.Equal(80, r.CollectionRatePct);                 // 80,060 / 100,060 = 79.9 -> 80
        Assert.Equal(8, r.FacilityCount);

        // Facility rows reconcile to the headline totals.
        Assert.Equal(r.Collected, r.Facilities.Sum(f => f.Collected));
        Assert.Equal(r.CurrentPeriodUnpaid, r.Facilities.Where(f => f.Unpaid.HasValue).Sum(f => f.Unpaid!.Value));
    }

    [Fact]
    public async Task AllTime_AggregatesEveryYear_AndReconciles()
    {
        var (handler, _) = Build();

        // One yearly report vs the All-time view. The mocks return the same figures for every year
        // (It.IsAny year), so All time must equal the single year × the number of aggregated years.
        var single = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, 2026, null, null), CancellationToken.None)).Value!;
        var all = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, 2026, null, null, AllTime: true), CancellationToken.None)).Value!;

        Assert.Equal("All time", all.PeriodLabel);
        Assert.Equal("All time", all.Frequency);

        var yearsCount = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year - 2020 + 1;
        Assert.Equal(single.Collected * yearsCount, all.Collected);
        Assert.Equal(single.CurrentPeriodUnpaid * yearsCount, all.CurrentPeriodUnpaid);

        // Merged facility rows still reconcile to the aggregated headline totals.
        Assert.Equal(all.Collected, all.Facilities.Sum(f => f.Collected));
        Assert.Equal(all.CurrentPeriodUnpaid, all.Facilities.Where(f => f.Unpaid.HasValue).Sum(f => f.Unpaid!.Value));
    }

    [Fact]
    public async Task AllTime_StartsAtTheTenantsFirstYear_NotTheSystemEpoch()
    {
        var (handler, reports) = Build();

        // A tenant whose records begin in 2025. The view used to build a full report for every year back to 2020 —
        // each one walking every facility — for years the office has no data in. It now starts where they started.
        reports.Setup(r => r.GetEarliestActivityYearAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2025);

        var single = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, 2026, null, null), CancellationToken.None)).Value!;
        var all = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, 2026, null, null, AllTime: true), CancellationToken.None)).Value!;

        // 2025 and 2026 only, and the money still reconciles.
        var years = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year - 2025 + 1;
        Assert.Equal(single.Collected * years, all.Collected);
        Assert.Equal(all.Collected, all.Facilities.Sum(f => f.Collected));
    }

    [Fact]
    public async Task AYearlyReport_CoversThatWholeYear_NotUpToTodaysMonth()
    {
        var (handler, reports) = Build();

        (int Year, int Month)? asked = null;
        reports.Setup(r => r.GetDelinquentStallsAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<FacilityCode?, int, int, bool, bool, CancellationToken>((_, y, m, _, _, _) => asked = (y, m))
            .ReturnsAsync(new List<DelinquentStallDto>());

        var dto = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, 2024, null, null), CancellationToken.None)).Value!;

        // The anchor is the month the figures stop BEFORE, so a 2024 report is anchored at January 2025 and therefore
        // covers December 2024. It used to borrow today's month — a 2024 report ended on 31 July 2024 and silently
        // dropped August to December from a printed government report.
        Assert.Equal((2025, 1), asked);

        // And the page states the span it was given, not the month it happens to be read in.
        Assert.Equal("December 2024", dto.AttentionSpanLabel);
    }

    [Fact]
    public async Task AReportForTheCurrentYear_StaysYearToDate()
    {
        var (handler, reports) = Build();

        (int Year, int Month)? asked = null;
        reports.Setup(r => r.GetDelinquentStallsAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<FacilityCode?, int, int, bool, bool, CancellationToken>((_, y, m, _, _, _) => asked = (y, m))
            .ReturnsAsync(new List<DelinquentStallDto>());

        var today = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today;
        var dto = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Yearly, today.Year, null, null), CancellationToken.None)).Value!;

        // The anchor is still January of next year — the repository clamps it to the last closed month, so the current
        // year is year-to-date rather than a projected January-to-December receivable.
        Assert.Equal((today.Year + 1, 1), asked);

        var lastClosed = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        Assert.Equal(lastClosed.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), dto.AttentionSpanLabel);
    }

    [Fact]
    public async Task ClosedExpiredAccounts_WithBalance_AreSummarized_SeparateFromDelinquency()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        // The two accounts that genuinely ended (₱32,910 each) are summarized for visibility...
        Assert.Equal(2, r.ClosedWithBalanceCount);
        Assert.Equal(65_820m, r.ClosedWithBalanceOutstanding);

        // ...but NOT folded into the record-based current delinquency/arrears lists.
        Assert.DoesNotContain(r.Delinquent, d => d.StallNo is "91" or "92");
        Assert.DoesNotContain(r.Arrears, d => d.StallNo is "91" or "92");
    }

    [Fact]
    public async Task LapsedAccounts_AreNotCountedAsClosedBalances_BecauseTheyAreStillCollected()
    {
        var (handler, reports) = Build();

        // A lapsed account's term ran out but the space was never handed over, so the tenant is ordinarily still
        // trading and the arrears figures already carry the debt. Counting it under closed balances as well stated
        // the same money twice: Cantilan read ₱1,905,300 of "closed / expired" beside a ₱519,880 follow-up total,
        // and 57 of those 58 accounts were the same live receivables over a longer span.
        _lastStalls!.Setup(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Closed("91", 32_910m, InactiveAccountState.Superseded),
                Closed("93", 33_300m, InactiveAccountState.Lapsed),
                Closed("94", 33_300m, InactiveAccountState.Lapsed),
            });

        var r = (await handler.Handle(
            new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        Assert.Equal(1, r.ClosedWithBalanceCount);
        Assert.Equal(32_910m, r.ClosedWithBalanceOutstanding);
    }

    [Fact]
    public async Task SplitsDelinquentFromArrears_ByMissedMonths()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var delinquent = Assert.Single(r.Delinquent);
        Assert.Equal("Rosa Magbanua", delinquent.Name);
        Assert.Equal(3, delinquent.UnpaidMonths);
        Assert.Equal(12_000m, delinquent.Balance);

        var arrears = Assert.Single(r.Arrears);
        Assert.Equal("Maria Velasco", arrears.Name);
        Assert.Equal(1, arrears.UnpaidMonths);
    }

    [Fact]
    public async Task PaidOnServiceFacilities_HaveNoUnpaid_AndFullRate()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var trm = r.Facilities.Single(f => f.Code == FacilityCode.TRM);
        Assert.True(trm.PaidOnService);
        Assert.Null(trm.Unpaid);
        Assert.Equal(100, trm.RatePct);
        Assert.Equal(60m, trm.Collected);
        Assert.Equal(2, trm.PaidRecords);

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.False(npm.PaidOnService);
        Assert.Equal(20_000m, npm.Unpaid);
    }

    // ── The market's electricity and water, counted as its revenue ────────────────────────────────────────────
    //
    // Asked for by the office: the market's electricity and water are its revenue, so its Collected should say so.
    // They are held on utility bills, which no stall-fee path writes to, so counting them adds nothing twice.
    //
    // These tests exist because the figures used to be shown beside the total instead of in it, and because the
    // change touches a headline money column, the percentage next to it, and the trend chart underneath.

    /// <summary>Sets the market's utility totals for every period the handler asks about.</summary>
    private static void WithUtilities(Mock<IFacilityReportsRepository> reports, decimal elec, decimal water, decimal due) =>
        reports.Setup(r => r.GetNpmUtilityTotalsAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((elec, water, due));

    [Fact]
    public async Task TheMarketsCollected_CountsItsElectricityAndWater()
    {
        var (handler, reports) = Build();
        WithUtilities(reports, elec: 500m, water: 300m, due: 119m);

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.Equal(80_800m, npm.Collected);        // 80,000 stall fees + 800 utilities
        Assert.Equal(20_119m, npm.Unpaid);           // 20,000 stall fees + 119 still due

        // The headline follows, and the rows still add up to it. A total that disagreed with its own rows is the
        // fault this report exists to avoid.
        Assert.Equal(80_860m, r.Collected);          // + TRM 60
        Assert.Equal(20_119m, r.CurrentPeriodUnpaid);
        Assert.Equal(r.Collected, r.Facilities.Sum(f => f.Collected));
        Assert.Equal(r.CurrentPeriodUnpaid, r.Facilities.Where(f => f.Unpaid.HasValue).Sum(f => f.Unpaid!.Value));
    }

    [Fact]
    public async Task TheBreakdownAddsUpToTheRowsTotal()
    {
        // What the expandable row prints: stall fee + fish + electricity + water must reconcile to Collected, because
        // an officer checks the parts against the total by hand.
        var (handler, reports) = Build();
        WithUtilities(reports, elec: 500m, water: 300m, due: 119m);

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        var d = npm.Detail!;
        Assert.Equal(500m, d.ElecCollected);
        Assert.Equal(300m, d.WaterCollected);
        Assert.Equal(119m, d.UtilityOutstanding);

        // 810 daily + 346 fish + 800 utilities, with the rest of the 80,000 being monthly payments the row does not
        // itemise; what matters is that nothing is counted twice and nothing named is missing.
        Assert.Equal(npm.Collected - d.DailyFeeCollected - d.FishCollected - d.ElecCollected - d.WaterCollected,
                     80_000m - 810m - 346m);
    }

    [Fact]
    public async Task TheRatePctIsComputedFromTheRowsOwnTwoFigures()
    {
        // The repository states 80 percent from the stall fees alone. Once utilities are part of the row, the
        // percentage has to be the row's own Collected over its own Billed, or it contradicts the numbers printed
        // beside it.
        var (handler, reports) = Build();
        WithUtilities(reports, elec: 20_000m, water: 0m, due: 0m);

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.Equal(100_000m, npm.Collected);
        Assert.Equal(20_000m, npm.Unpaid);
        Assert.Equal(83, npm.RatePct);               // 100,000 / 120,000 = 83.3 -> 83, not the repository's 80
    }

    [Fact]
    public async Task WithNoUtilityBill_EveryFigureIsExactlyWhatItWasBefore()
    {
        // The guard on the whole change: an office with no utility bills, and every other facility in any case, must
        // be byte-for-byte unaffected. The rate in particular is left to the repository rather than recomputed, since
        // the two are not derived the same way.
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.Equal(80_000m, npm.Collected);
        Assert.Equal(20_000m, npm.Unpaid);
        Assert.Equal(80, npm.RatePct);
        Assert.Equal(80_060m, r.Collected);
    }

    [Fact]
    public async Task AWeeklyReport_CountsStallFeesAlone()
    {
        // A utility bill is billed for a MONTH and carries no week of its own, so folding one into a week would
        // overstate that week. Weekly stays what it always was.
        var (handler, reports) = Build();
        WithUtilities(reports, elec: 500m, water: 300m, due: 119m);

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Weekly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.Equal(80_000m, npm.Collected);
        Assert.Equal(20_000m, npm.Unpaid);
        Assert.Equal(0m, npm.Detail!.ElecCollected);
        Assert.Equal(0m, npm.Detail!.WaterCollected);
    }

    [Fact]
    public async Task EveryBarOfTheTrendCountsUtilities_NotOnlyTheSelectedOne()
    {
        // Otherwise the selected month stands higher than every month before it purely because it was the only one
        // counting electricity and water, which reads as a rise in collection that never happened.
        var (handler, reports) = Build();
        WithUtilities(reports, elec: 500m, water: 300m, due: 0m);

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        Assert.All(r.Trend, point => Assert.True(
            point.Collected >= 800m,
            $"{point.Label} carries {point.Collected}, so it is not counting the market's utilities"));
    }

    [Fact]
    public async Task SingleFacilityScope_OnlyReturnsThatFacility()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, FacilityCode.NPM), CancellationToken.None)).Value!;

        Assert.Equal(1, r.FacilityCount);
        var only = Assert.Single(r.Facilities);
        Assert.Equal(FacilityCode.NPM, only.Code);
        Assert.Equal(80_000m, r.Collected);   // TRM excluded from scope
    }

    [Fact]
    public async Task NpmRow_HasDetailBreakdown_FishAndFullMonthCoverage()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.Equal(27, npm.PaidRecords);               // ₱810 daily fee ÷ ₱30 = 27 daily collections (not stall count)
        var d = npm.Detail!;
        Assert.NotNull(d);
        Assert.Equal(810m, d.DailyFeeCollected);
        Assert.Equal(346m, d.FishCollected);
        Assert.Equal(346m, d.FishKilos);                 // ₱1/kg → kilos == fish amount
        Assert.Equal(20_000m, d.PeriodBalance);          // selected-period assessed − collected
        Assert.Equal(2_700m, d.FullMonthCoverage);       // 3 occupied stalls × ₱900 (no absent days)
        Assert.Equal(1_300m, d.FullMonthCoverageBalance);// per stall: max(0,900-0)+max(0,900-500)+max(0,900-900)=1,300
        Assert.Equal(0m, d.ExcusedAmount);               // no absent days → nothing excused

        // Non-NPM facilities carry no detail.
        Assert.Null(r.Facilities.Single(f => f.Code == FacilityCode.TRM).Detail);
    }

    [Fact]
    public async Task Trend_SelectedBarMatchesKpi_AndFoldsServiceIntoPriorMonths()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        Assert.Equal(6, r.Trend.Count);                       // Monthly = last 6 months

        var selected = r.Trend.Single(p => p.IsSelected);
        Assert.Equal(r.Trend[^1], selected);                  // selected = the latest bar
        Assert.Equal(r.Collected, selected.Collected);        // reconciles to the Collected KPI (incl. service)
        Assert.Equal(r.CurrentPeriodUnpaid, selected.Unpaid);

        // Earlier months fold in the paid-on-service facilities (TRM = 2 trips × ₱30 = ₱60 in the mock).
        Assert.All(r.Trend.Where(p => !p.IsSelected), p => Assert.Equal(60m, p.Collected));

        // Month-over-month: previous-period collected = the bar immediately before the selected one.
        Assert.Equal(r.Trend[^2].Collected, r.CollectedPreviousPeriod);
        Assert.Equal(60m, r.CollectedPreviousPeriod);
    }

    [Fact]
    public async Task MapsRecentRecords_FromFeed()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var rec = Assert.Single(r.RecentRecords);
        Assert.Equal("OR-9", rec.Reference);
        Assert.Equal("Luz Cano", rec.Payor);
        Assert.Equal("Daily Fee", rec.Method);
        Assert.Equal(930m, rec.Amount);
    }
}
