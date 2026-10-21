using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Testing.Support;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

public sealed class NpmMonthSettlementBridgeTests
{
    [Fact]
    public async Task Mobile_per_payee_overlay_uses_stable_source_id_and_does_not_add_office_cash_to_collector_totals()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25, businessDate: new(2025, 8, 26));
        Assert.True((await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!))).IsSuccess);
        var collector = EEMOCantilanSDS.Domain.Entities.Users.CollectorUser.Create("Fixture collector", "TEST", "fixture", null, null,
            new EEMOCantilanSDS.Domain.Common.HashedPassword("fixture-only-hash"));
        collector.FacilityAssignments.Add(EEMOCantilanSDS.Domain.Entities.Users.CollectorFacilityAssignment.Create(collector.Id,
            f.Month.FacilityId, EEMOCantilanSDS.Domain.Enums.FacilityCode.NPM));
        var collectors = new Moq.Mock<EEMOCantilanSDS.Application.Common.Interface.Persistence.ICollectorRepository>();
        collectors.Setup(x => x.GetByIdAsync(collector.Id, Moq.It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        var report = new EEMOCantilanSDS.Application.Dtos.Mobile.MobileCollectorReportDto(2025, 8, f.Month.PeriodStart, f.BusinessDate, true,
            new(0, 25, 0, 1, 0, 1, 0, 0, 1), [], [],
            [new("Same name is irrelevant", null, EEMOCantilanSDS.Domain.Enums.FacilityCode.NPM, "Market", "A-1", "Vegetable",
                EEMOCantilanSDS.Domain.Enums.PaymentStatus.Partial, 900, 875, 25, 0, 0, null, null, f.Stall.Id)], [], []);
        var reader = new Moq.Mock<EEMOCantilanSDS.Application.Common.Interface.Persistence.ICollectorMobileQueries>();
        reader.Setup(x => x.GetCollectorReportAsync(collector.Id, Moq.It.IsAny<IReadOnlyCollection<EEMOCantilanSDS.Domain.Enums.FacilityCode>>(),
            Moq.It.IsAny<DateOnly>(), Moq.It.IsAny<DateOnly>(), Moq.It.IsAny<CancellationToken>())).ReturnsAsync(report);
        var facts = new Moq.Mock<EEMOCantilanSDS.Application.Common.Interface.Persistence.ICollectorCollectionFacts>();
        facts.Setup(x => x.GetMyCollectionsAsync(Moq.It.IsAny<DateOnly>(), Moq.It.IsAny<DateOnly>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(EEMOCantilanSDS.Application.Common.Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectorCollectionFactDto>>.Success([]));
        var user = new Moq.Mock<EEMOCantilanSDS.Application.Common.Interface.Services.ICurrentUserService>();
        user.SetupGet(x => x.CollectorId).Returns(collector.Id);
        var handler = new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport.GetCollectorReportQueryHandler(
            collectors.Object, reader.Object, facts.Object, user.Object,
            new FixedClock(new DateTime(2025, 8, 26, 4, 0, 0, DateTimeKind.Utc)), f.Workflow);
        var result = await handler.Handle(new(null, 2025, 8), default);
        Assert.True(result.IsSuccess, result.Error);
        var payee = Assert.Single(result.Value!.Payees);
        Assert.Equal(900, payee.AmountPaid); Assert.Equal(0, payee.Balance);
        Assert.Equal(0, result.Value.Totals.OutstandingAmount); Assert.Equal(0, result.Value.Totals.CollectedAmount);
    }
    [Fact]
    public async Task Mobile_round_and_head_quote_share_terminal_amount_and_whole_payment_does_not_fabricate_daily_rows()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25,
            businessDate: new(2025, 8, 26));
        f.Stall.SetSection(EEMOCantilanSDS.Domain.Enums.MarketSection.VegetableArea, null);
        await f.Db.SaveChangesAsync();
        var clock = new FixedClock(new DateTime(2025, 8, 26, 4, 0, 0, DateTimeKind.Utc));
        var repository = new StallRepository(f.Db, f.Rates, clock, new NpmMonthSettlementBridge(f.Legacy, f.Workflow, clock), f.Workflow);
        var before = await repository.GetMobileNpmCollectionAsync(2025, 8, f.BusinessDate, default);
        var row = Assert.Single(before.Stalls);
        Assert.Equal(25, row.MonthlyRentQuote!.EligibleAmount);
        Assert.Equal((await f.Quote()).Value!.Outstanding, row.MonthlyRentQuote.Outstanding);
        Assert.Equal(25, before.PendingTodayAmount);
        var saved = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(saved.IsSuccess, saved.Error);
        var after = await repository.GetMobileNpmCollectionAsync(2025, 8, f.BusinessDate, default);
        row = Assert.Single(after.Stalls);
        Assert.Equal(900, row.MonthCollectedAmount); Assert.Equal(25, row.DaysCollected);
        Assert.Equal(0, row.DaysMissed); Assert.Equal(0, after.PendingTodayAmount);
        Assert.False(row.IsCollectedToday); Assert.Empty(row.UncollectedDays!);
        Assert.Equal(25, f.Db.DailyCollections.Count());
    }
    [Fact]
    public async Task Whole_payment_cash_event_keeps_real_date_and_src_without_inventing_an_installment_day()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25);
        var saved = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(saved.IsSuccess, saved.Error);
        var original = Assert.Single(await f.Workflow.ReadCashEventsAsync(f.Stall.Id, f.Month.PeriodStart));
        Assert.Equal(saved.Value!.CollectionId, original.CollectionId);
        Assert.Equal(f.BusinessDate, original.BusinessDate);
        Assert.Null(original.InstallmentDate);
        Assert.Equal(25, original.Net);
        Assert.True((await f.Workflow.ReverseAsync(original.CollectionId, Guid.NewGuid(), "Fixture reversal")).IsSuccess);
        var reversed = Assert.Single(await f.Workflow.ReadCashEventsAsync(f.Stall.Id, f.Month.PeriodStart));
        Assert.Equal(25, reversed.Received);
        Assert.Equal(0, reversed.Net);
        Assert.Equal(original.ReferenceCode, reversed.ReferenceCode);
        Assert.Equal(25, f.Db.DailyCollections.Count());
    }

    [Fact]
    public async Task Active_remittance_coverage_prevents_month_reversal_without_changing_cash_or_balance()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25);
        var saved = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(saved.IsSuccess, saved.Error);
        f.Db.CollectionRemittanceCoverages.Add(EEMOCantilanSDS.Domain.Entities.Revenue.CollectionRemittanceCoverage.Cover(
            f.TenantId, Guid.NewGuid(), saved.Value!.CollectionId, 25));
        await f.Db.SaveChangesAsync();
        var result = await f.Workflow.ReverseAsync(saved.Value.CollectionId, Guid.NewGuid(), "Fixture reconciliation");
        Assert.False(result.IsSuccess); Assert.Contains("Remitted cash", result.Error);
        Assert.Empty(f.Db.CollectionCorrections);
        Assert.Equal(0, (await f.Workflow.ReadPositionAsync(f.Stall.Id, f.Month.PeriodStart))!.Outstanding);
        Assert.Single(f.Db.Collections);
    }
    [Fact]
    public async Task Historical_cutoff_does_not_pull_later_payment_or_credit_back_into_prior_day()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25);
        var cutoff = new DateOnly(2026, 10, 16);
        Assert.Null(await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, new(2026, 10, 15)));
        Assert.Equal(25, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, cutoff))!.Outstanding);
        Assert.True((await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!))).IsSuccess);
        Assert.Equal(0, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate))!.Outstanding);
        Assert.Equal(25, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, cutoff))!.Outstanding);
        Assert.True((await f.Workflow.ReviseCreditsAsync(f.Stall.Id, f.Month.PeriodStart, 10,
            "{\"reference\":\"fixture-credit\"}", f.Month.SettlementVersion, "Fixture credit decision")).IsSuccess);
        Assert.Equal(0, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, cutoff))!.Credits);
        Assert.Equal(10, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate))!.HistoricalExcess);
    }

    [Fact]
    public async Task Canonical_activity_preserves_month_period_and_real_collection_context_without_daily_receipt()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25);
        var saved = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(saved.IsSuccess, saved.Error);
        var recordedSpace = f.Stall.StallNo;
        f.Db.Entry(f.Stall).Property(x => x.StallNo).CurrentValue = "RENAMED-AFTER-PAYMENT";
        await f.Db.SaveChangesAsync();
        var activity = await new CollectionActivityReader(f.Db).GetAsync(f.TenantId, f.BusinessDate, f.BusinessDate);
        var row = Assert.Single(activity, x => x.CollectionId == saved.Value!.CollectionId);
        Assert.Equal(25, row.Amount); Assert.Equal(EEMOCantilanSDS.Domain.Enums.FacilityCode.NPM, row.Facility);
        var line = Assert.Single(row.Lines);
        Assert.Equal("Stall " + recordedSpace, line.Description);
        Assert.Equal("NpmRentMonth", line.SourceKind); Assert.Equal(2025, line.PeriodYear); Assert.Equal(8, line.PeriodMonth);
        Assert.Equal(f.BusinessDate, row.BusinessDate); Assert.Equal(25, f.Db.DailyCollections.Count());
    }
    internal static NpmMonthSettlementBridge Bridge(NpmMonthWorkflowFixture f) => new(f.Legacy, f.Workflow,
        new FixedClock(new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc)));

    [Theory]
    [InlineData(900, 25, 25)]
    [InlineData(1000, 28, 20)]
    public async Task Readers_return_exact_terminal_balance_and_only_one_remaining_installment(decimal rent, int days, decimal remaining)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(rent, 35, paidDays: days);
        var bridge = Bridge(f);
        var whole = await bridge.ComputeWholeMonthPayableAsync(f.Stall, 2025, 8, default);
        Assert.Equal(remaining, whole.Amount);
        Assert.Equal(f.Month.Id, whole.MonthSourceId);
        Assert.Equal(remaining, (await bridge.ComputePayableAsync(f.Stall, 2025, 8, default)).Amount);
        Assert.Single(await bridge.GetPayableDaysAsync(f.Stall, 2025, 8, default));
        Assert.Equal(remaining, (await bridge.ComputePayableForDaysAsync(f.Stall, 2025, 8, 3, default)).Amount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.SettleWholeMonthAsync(f.Stall, 2025, 8, null, "Head", default));
    }

    [Fact]
    public async Task Activated_source_never_falls_back_when_runtime_is_disabled()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(enabled: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bridge(f).ComputeWholeMonthPayableAsync(f.Stall, 2025, 8, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bridge(f).SettleUnpaidDaysAsync(f.Stall, 2025, 8, null, "Head", default));
    }

    [Fact]
    public async Task Completed_month_has_no_daily_arrears_or_extra_day_31_charge()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 30, paidDays: 30);
        Assert.Empty(await Bridge(f).GetPayableDaysAsync(f.Stall, 2025, 8, default));
        Assert.Equal(0, (await Bridge(f).ComputePayableAsync(f.Stall, 2025, 8, default)).Amount);
    }

    [Fact]
    public async Task Account_history_and_outstanding_reader_observe_month_payment_and_reversal_once()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25, year: 2026);
        var clock = new FixedClock(new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc));
        var ledger = new PaymentRepository(f.Db, f.Rates, clock, f.Workflow);
        var before = await ledger.GetOutstandingMonthsAsync(f.Stall.Id, f.Month.ContractId, f.Month.PeriodStart, default);
        Assert.Equal(25, Assert.Single(before, x => x.Period == "2026-08").BalanceDue);
        var saved = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(saved.IsSuccess, saved.Error);
        var after = await ledger.GetOutstandingMonthsAsync(f.Stall.Id, f.Month.ContractId, f.Month.PeriodStart, default);
        Assert.DoesNotContain(after, x => x.Period == "2026-08");
        var history = await ledger.GetPaymentHistoryAsync(f.Stall.Id, default);
        var august = Assert.Single(history, x => x.Period == "2026-08");
        Assert.Equal(900, august.AmountPaid); Assert.Equal(0, august.BalanceDue);
        Assert.Equal(25, f.Db.DailyCollections.Count());
        Assert.True((await f.Workflow.ReverseAsync(saved.Value!.CollectionId, Guid.NewGuid(), "Fixture reversal")).IsSuccess);
        var reversed = await ledger.GetOutstandingMonthsAsync(f.Stall.Id, f.Month.ContractId, f.Month.PeriodStart, default);
        Assert.Equal(25, Assert.Single(reversed, x => x.Period == "2026-08").BalanceDue);
    }
}
