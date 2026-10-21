using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmRentMonthFinancialTests
{
    [SkippableTheory]
    [InlineData(2, 28, 60)]
    [InlineData(9, 30, 0)]
    [InlineData(8, 30, 0)]
    public async Task Monthly_900_cap_survives_February_30_and_31_day_months(int calendarMonth, int days, decimal remaining)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var period = new DateOnly(2025, calendarMonth, 1);
        var w = await Seed(900, 30, days, period);
        await using var db = fixture.Create(w.Tenant); var flow = Workflow(db, w);
        var whole = await flow.QuoteAsync(w.Stall, period, BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(whole.IsSuccess, whole.Error); Assert.Equal(remaining, whole.Value!.EligibleAmount);
        if (remaining > 0) Assert.True((await flow.PostAsync(Payment(w, whole.Value))).IsSuccess);
        var daily = await flow.QuoteAsync(w.Stall, period, BusinessDate, NpmRentPaymentScope.DailyInstallment,
            installmentDate: period.AddMonths(1).AddDays(-1));
        Assert.True(daily.IsSuccess, daily.Error); Assert.Equal(0, daily.Value!.EligibleAmount);
        Assert.False((await flow.PostAsync(Payment(w, daily.Value) with { Amount = 30 })).IsSuccess);
        Assert.Equal(days, await db.DailyCollections.CountAsync());
        Assert.Equal(900, await db.DailyCollections.SumAsync(x => x.DailyFee) + await db.CollectionAllocations.SumAsync(x => x.Amount));
        Assert.Equal(remaining > 0 ? 1 : 0, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Batched_reports_keep_cash_credit_cutoffs_and_tenant_sources_disjoint()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25); var otherTenant = await Seed();
        var clock = new ReportClock(new(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc));
        await using var db = fixture.Create(w.Tenant);
        var facility = await db.Facilities.SingleAsync();
        var stall = Stall.Create(facility.Id, "BATCH-2", 900, ApplicableFees.DailyRental, municipalityId: w.Tenant);
        var contract = Contract.Create(stall.Id, "Second synthetic occupant", "Fixture", new(2025, 1, 1), 3, 900);
        db.AddRange(stall, contract); await db.SaveChangesAsync();
        var month = Prepare(facility, stall, contract, w.Actor); db.Add(month); await db.SaveChangesAsync();
        var flow = Workflow(db, w, clock); var period = new DateOnly(2025, 8, 1);
        Assert.True((await flow.ApplyReviewedCreditAsync(new(Guid.NewGuid(), w.Stall, period, 3, 10, "Synthetic decision", "Approved credit"))).IsSuccess);
        var q1 = await flow.QuoteAsync(w.Stall, period, BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(q1.IsSuccess, q1.Error); var p1 = await flow.PostAsync(Payment(w, q1.Value!)); Assert.True(p1.IsSuccess, p1.Error);
        var q2 = await flow.QuoteAsync(stall.Id, period, BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(q2.IsSuccess, q2.Error);
        Assert.True((await flow.PostAsync(Payment(w with { Stall = stall.Id }, q2.Value!))).IsSuccess);
        Assert.Equal(915, (await flow.ReadCashMovementsAsync(facility.Id, BusinessDate, BusinessDate)).Sum(x => x.Amount));
        clock.UtcNow = clock.UtcNow.AddDays(1);
        Assert.True((await flow.ReverseAsync(p1.Value!.CollectionId, Guid.NewGuid(), "Later correction")).IsSuccess);
        var known = await flow.ReadReportPositionsAsync([w.Stall, stall.Id, otherTenant.Stall], period, BusinessDate);
        var latest = await flow.ReadReportPositionsAsync([w.Stall, stall.Id, otherTenant.Stall], period, clock.PhilippineToday);
        Assert.Equal(2, known.Count); Assert.Equal(2, latest.Count);
        Assert.Equal(0, known[w.Stall]!.Position.Outstanding); Assert.Equal(15, latest[w.Stall]!.Position.Outstanding);
        Assert.Equal(10, latest[w.Stall]!.Position.Credits); Assert.Equal(0, latest[stall.Id]!.Position.Outstanding);
        Assert.Equal(-15, (await flow.ReadCashMovementsAsync(facility.Id, clock.PhilippineToday, clock.PhilippineToday)).Sum(x => x.Amount));
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Concurrent_reversals_reopen_only_the_original_amount_once()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        Guid collectionId;
        await using (var setup = fixture.Create(w.Tenant))
        {
            var flow = Workflow(setup, w);
            var quote = await flow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth);
            Assert.True(quote.IsSuccess, quote.Error);
            var posted = await flow.PostAsync(Payment(w, quote.Value!)); Assert.True(posted.IsSuccess, posted.Error);
            collectionId = posted.Value!.CollectionId;
        }
        await using var a = fixture.Create(w.Tenant); await using var b = fixture.Create(w.Tenant);
        var results = await Task.WhenAll(Workflow(a, w).ReverseAsync(collectionId, Guid.NewGuid(), "First reviewed reversal"),
            Workflow(b, w).ReverseAsync(collectionId, Guid.NewGuid(), "Second reviewed reversal"));
        Assert.Single(results, x => x.IsSuccess);
        await using var verify = fixture.Create(w.Tenant);
        Assert.Single(await verify.CollectionCorrections.ToListAsync()); Assert.Single(await verify.Collections.ToListAsync());
        Assert.Equal(-25, await verify.CollectionCorrectionAllocations.SumAsync(x => x.FinancialEffectAmount));
        Assert.Equal(25, (await Workflow(verify, w).ReadPositionAsync(w.Stall, new(2025, 8, 1)))!.Outstanding);
    }

    [SkippableFact]
    public async Task Facility_report_preserves_closed_period_cash_and_coverage_after_a_later_reversal()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        var clock = new ReportClock(new(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc));
        await using var db = fixture.Create(w.Tenant);
        var stall = await db.Stalls.Include(x => x.Contracts).Include(x => x.Facility).SingleAsync();
        db.NpmRentMonths.RemoveRange(await db.NpmRentMonths.ToListAsync());
        stall.SetSection(MarketSection.VegetableArea, null);
        var source = Prepare(stall.Facility!, stall, stall.Contracts.Single(), w.Actor, new(2026, 10, 1), true);
        db.Add(source); await db.SaveChangesAsync(); w = w with { Month = source.Id };
        var workflow = Workflow(db, w, clock);
        var quote = await workflow.QuoteAsync(w.Stall, source.PeriodStart, BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(quote.IsSuccess, quote.Error);
        var posted = await workflow.PostAsync(Payment(w, quote.Value!)); Assert.True(posted.IsSuccess, posted.Error);
        var report = await new EEMOCantilanSDS.Infrastructure.Repositories.FacilityReportsRepository(db, new Rates(w.Daily), clock, workflow)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 10, null, default);
        Assert.Equal(900, report.TotalRevenue); Assert.Equal(0, report.PendingPaymentAmount);
        Assert.Equal(900, Assert.Single(report.StallCompliance).FullPeriodRentObligation);
        Assert.Equal(900, report.StallCompliance.Single().AmountPaid);
        Assert.Equal(900, report.SectionBreakdown.Sum(x => x.Revenue));
        Assert.Equal(900, report.FeeTypeBreakdown!.DailyFeeAmount); Assert.Equal(0, report.FeeTypeBreakdown.PaidDayRecords);
        clock.UtcNow = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
        Assert.True((await workflow.ReverseAsync(posted.Value!.CollectionId, Guid.NewGuid(), "Later verified reversal")).IsSuccess);
        var historical = await new EEMOCantilanSDS.Infrastructure.Repositories.FacilityReportsRepository(db, new Rates(w.Daily), clock, workflow)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 10, null, default);
        Assert.Equal(report.TotalRevenue, historical.TotalRevenue);
        Assert.Equal(0, historical.StallCompliance.Single().Balance);
        Assert.Equal(900, (await workflow.ReadPositionAsync(w.Stall, source.PeriodStart))!.Outstanding);
        Assert.Equal(-900, (await workflow.ReadCashMovementsAsync(stall.FacilityId, new(2026, 11, 1), new(2026, 11, 30))).Sum(x => x.Amount));
    }

    [SkippableFact]
    public async Task Remitted_month_cash_cannot_be_reversed_or_replaced_and_remittance_creates_no_revenue()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        await using var db = fixture.Create(w.Tenant);
        var facility = await db.Facilities.SingleAsync();
        var collector = EEMOCantilanSDS.Domain.Entities.Users.CollectorUser.Create("Synthetic collector", "TEST-NPM", "fixture-collector", null, null,
            new EEMOCantilanSDS.Domain.Common.HashedPassword("fixture-only"), w.Tenant);
        collector.FacilityAssignments.Add(EEMOCantilanSDS.Domain.Entities.Users.CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
        db.Add(collector); await db.SaveChangesAsync();
        var collectorFlow = Workflow(db, w, collectorId: collector.Id);
        var quote = await collectorFlow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(quote.IsSuccess, quote.Error);
        var posted = await collectorFlow.PostAsync(Payment(w, quote.Value!)); Assert.True(posted.IsSuccess, posted.Error);
        var remittance = EEMOCantilanSDS.Domain.Entities.Revenue.CollectionRemittance.Record(w.Tenant, collector.Id, BusinessDate, BusinessDate, BusinessDate,
            null, 25, 25, 1, "Synthetic turnover", null, Guid.NewGuid(), "synthetic-intent", "Head", w.Actor.ToString(), new Clock().UtcNow);
        db.Add(remittance);
        db.Add(EEMOCantilanSDS.Domain.Entities.Revenue.CollectionRemittanceCoverage.Cover(w.Tenant, remittance.Id, posted.Value!.CollectionId, 25));
        await db.SaveChangesAsync();
        var head = Workflow(db, w);
        Assert.False((await head.ReverseAsync(posted.Value.CollectionId, Guid.NewGuid(), "Review remitted cash")).IsSuccess);
        Assert.False((await head.ReplaceReviewedAsync(new(Guid.NewGuid(), posted.Value.CollectionId,
            (await db.NpmRentMonths.SingleAsync()).SettlementVersion, 25, "Office", "Review"))).IsSuccess);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionCorrections.ToListAsync());
        Assert.Equal(25, await db.CollectionAllocations.SumAsync(x => x.Amount));
        Assert.Equal(25, (await db.CollectionRemittances.SingleAsync()).RemittedAmount);
        // Coverage is seeded here; full remittance-command policy remains covered by RemittanceWorkflowTests.
    }

    [SkippableFact]
    public async Task Concurrent_month_preparation_creates_one_obligation_and_no_cash()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        await using (var setup = fixture.Create(w.Tenant))
        { setup.NpmRentMonths.RemoveRange(await setup.NpmRentMonths.ToListAsync()); await setup.SaveChangesAsync(); }
        async Task<bool> Insert()
        {
            await using var db = fixture.Create(w.Tenant);
            var stall = await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts).SingleAsync();
            var month = Prepare(stall.Facility!, stall, stall.Contracts.Single(), w.Actor);
            db.Add(month);
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
            {
                Assert.Equal("IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart", pg.ConstraintName); return false;
            }
        }
        Assert.Equal(1, (await Task.WhenAll(Insert(), Insert())).Count(x => x));
        await using var verify = fixture.Create(w.Tenant);
        Assert.Single(await verify.NpmRentMonths.ToListAsync());
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task Concurrent_same_operation_retry_issues_one_SRC_and_can_replay_after_conflict()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        await using var a = fixture.Create(w.Tenant); await using var b = fixture.Create(w.Tenant);
        var quoted = await Workflow(a, w).QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(quoted.IsSuccess, quoted.Error); var payment = Payment(w, quoted.Value!);
        var results = await Task.WhenAll(Workflow(a, w).PostAsync(payment), Workflow(b, w).PostAsync(payment));
        Assert.Contains(results, x => x.IsSuccess);
        await using var verify = fixture.Create(w.Tenant);
        var replay = await Workflow(verify, w).PostAsync(payment);
        Assert.True(replay.IsSuccess, replay.Error); Assert.True(replay.Value!.Existing);
        Assert.Single(await verify.Collections.ToListAsync()); Assert.Single(await verify.PostingOperations.ToListAsync());
        Assert.Equal(25, await verify.CollectionAllocations.SumAsync(x => x.Amount));
        Assert.Single((await verify.Collections.Select(x => x.ReferenceCode).ToListAsync()).Distinct());
    }

    [SkippableFact]
    public async Task Cross_period_reversal_preserves_as_known_income_and_actual_cash_dates()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        var clock = new ReportClock(new(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc));
        await using var db = fixture.Create(w.Tenant); var workflow = Workflow(db, w, clock);
        var quote = await workflow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(quote.IsSuccess, quote.Error);
        var posted = await workflow.PostAsync(Payment(w, quote.Value!)); Assert.True(posted.IsSuccess, posted.Error);
        clock.UtcNow = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
        Assert.True((await workflow.ReverseAsync(posted.Value!.CollectionId, Guid.NewGuid(), "Later verified correction")).IsSuccess);
        Assert.Equal(0, (await workflow.ReadAsOfPositionAsync(w.Stall, quote.Value!.PeriodStart, new(2026, 10, 31)))!.Outstanding);
        Assert.Equal(25, (await workflow.ReadAsOfPositionAsync(w.Stall, quote.Value.PeriodStart, new(2026, 11, 1)))!.Outstanding);
        var facility = await db.Facilities.SingleAsync();
        Assert.Equal(25, (await workflow.ReadCashMovementsAsync(facility.Id, new(2026, 10, 1), new(2026, 10, 31))).Sum(x => x.Amount));
        Assert.Equal(-25, (await workflow.ReadCashMovementsAsync(facility.Id, new(2026, 11, 1), new(2026, 11, 30))).Sum(x => x.Amount));
        var income = new GetCanonicalMonthlyIncomeQueryHandler(db, new Actor(w), new Tenant(w.Tenant), clock);
        var known = await income.Handle(new(2026, 10, CanonicalReportingBasis.AsOf,
            new DateTimeOffset(2026, 10, 31, 15, 59, 59, TimeSpan.Zero)), default);
        var latest = await income.Handle(new(2026, 10, CanonicalReportingBasis.LatestCorrected), default);
        Assert.True(known.IsSuccess, known.Error); Assert.True(latest.IsSuccess, latest.Error);
        Assert.Equal(25, known.Value!.GrandTotal.NetCollected); Assert.Equal(0, latest.Value!.GrandTotal.NetCollected);
        Assert.Equal(-25, latest.Value.GrandTotal.CorrectionEffectDatedOutsidePeriod);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
    }

    [SkippableFact]
    public async Task Two_hundred_report_positions_have_a_bounded_read_query_budget()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        await using (var setup = fixture.Create(w.Tenant))
        {
            var facility = await setup.Facilities.SingleAsync();
            var pairs = Enumerable.Range(2, 199).Select(i => {
                var stall = Stall.Create(facility.Id, "S-" + i, 900, ApplicableFees.DailyRental, municipalityId: w.Tenant);
                return (Stall: stall, Contract: Contract.Create(stall.Id, "Synthetic " + i, "Fixture", new(2025, 1, 1), 3, 900));
            }).ToArray();
            foreach (var pair in pairs) setup.AddRange(pair.Stall, pair.Contract);
            await setup.SaveChangesAsync();
            foreach (var pair in pairs) setup.Add(Prepare(facility, pair.Stall, pair.Contract, w.Actor));
            await setup.SaveChangesAsync();
        }
        var counter = new QueryCounter();
        await using var db = fixture.Create(w.Tenant, counter); var workflow = Workflow(db, w);
        var stalls = await db.NpmRentMonths.Select(x => x.StallId).ToListAsync();
        counter.Reads = 0; var timer = Stopwatch.StartNew(); decimal outstanding = 0;
        foreach (var row in (await workflow.ReadReportPositionsAsync(stalls, new(2025, 8, 1), BusinessDate)).Values)
            outstanding += row!.Position.Outstanding;
        timer.Stop(); output.WriteLine($"Report positions: {stalls.Count}; SQL reads: {counter.Reads}; elapsed-ms: {timer.ElapsedMilliseconds}");
        Assert.Equal(180000, outstanding); Assert.Equal(200, stalls.Count);
        Assert.True(counter.Reads <= 12, $"Batched report exceeded budget: {counter.Reads} reads for 200 sources.");
        Assert.Empty(await db.Collections.ToListAsync());
    }

    private static NpmRentMonth Prepare(Facility facility, Stall stall, Contract contract, Guid actor, DateOnly? periodStart = null, bool monthlyDue = false)
    {
        var utc = new Clock().UtcNow.AddMinutes(-1);
        var period = periodStart ?? new DateOnly(2025, 8, 1);
        var policy = monthlyDue ? JsonSerializer.Serialize(new { dailyInstallmentsApproved = true, monthlyObligationDueFrom = period.ToString("yyyy-MM-dd") }, Json)
            : "{\"dailyInstallmentsApproved\":true}";
        var month = NpmRentMonth.Prepare(facility, stall, contract, period, BusinessDate,
            new(900, 0, NpmMonthBasis.RentGoal, period, "Synthetic approved policy", policy, "{\"approved\":true}"), actor, "Synthetic Head", "SuperAdmin", utc).Month;
        month.Reconcile(1, actor, "Synthetic Head", "SuperAdmin", utc,
            JsonSerializer.Serialize(new NpmRentMonthOpeningEvidence(1, [], [], NpmEmptyFingerprint()), Json));
        month.ActivateSettlement(2, actor, "Synthetic Head", "SuperAdmin", utc, "Disposable fixture activation only");
        return month;
    }
    private static string NpmEmptyFingerprint() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("[]")));
    private sealed class ReportClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
        public DateTime PhilippineNow => PhilippineTime.ToPhilippineTime(UtcNow);
        public DateOnly PhilippineToday => DateOnly.FromDateTime(PhilippineNow);
    }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Reads++; return ValueTask.FromResult(result); }
    }
    private sealed class FailReplacementSave : SaveChangesInterceptor
    {
        public bool ReversalSeen { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var context = data.Context!;
            if (context.ChangeTracker.Entries<EEMOCantilanSDS.Domain.Entities.Revenue.CollectionCorrection>().Any(x => x.State == EntityState.Added))
                ReversalSeen = true;
            if (ReversalSeen && context.ChangeTracker.Entries<EEMOCantilanSDS.Domain.Entities.Revenue.Collection>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Synthetic replacement-save failure after reversal.");
            return ValueTask.FromResult(result);
        }
    }
}
