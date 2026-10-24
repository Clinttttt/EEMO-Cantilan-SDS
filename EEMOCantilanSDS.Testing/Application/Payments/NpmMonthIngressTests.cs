using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

public sealed class NpmMonthIngressTests
{
    [Fact]
    public async Task Weekly_schedule_and_raw_override_restore_cannot_rewrite_an_activated_month_credit_basis()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(businessDate: new(2025, 8, 10));
        var schedules = new Mock<INpmOperatingScheduleRepository>(MockBehavior.Strict);
        var closures = new Mock<INpmMarketClosureRepository>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock = new FixedClock(new DateTime(2025, 8, 10, 4, 0, 0, DateTimeKind.Utc));
        var schedule = new EEMOCantilanSDS.Application.Command.Payments.SetNpmOperatingSchedule.SetNpmOperatingScheduleCommandHandler(
            schedules.Object, f.User, clock, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, f.Workflow);
        var result = await schedule.Handle(new(f.BusinessDate, [DayOfWeek.Sunday], "Fixture approved schedule"), default);
        Assert.False(result.IsSuccess); Assert.Contains("activated month", result.Error);
        var restore = new EEMOCantilanSDS.Application.Command.Payments.RestoreNpmOperatingDay.RestoreNpmOperatingDayCommandHandler(
            closures.Object, f.User, clock, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, f.Workflow);
        Assert.False((await restore.Handle(new(f.BusinessDate, "Fixture reopening"), default)).IsSuccess);
        schedules.VerifyNoOtherCalls(); closures.VerifyNoOtherCalls(); uow.VerifyNoOtherCalls();
        Assert.Empty(f.Db.AuditLogs); Assert.Empty(f.Db.Collections);
    }
    [Fact]
    public async Task Frozen_offline_daily_terminal_payment_replays_once_and_returns_same_source_reference()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25, asCollector: true);
        var quote = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate,
            NpmRentPaymentScope.DailyInstallment, installmentDate: new(2025, 8, 26));
        Assert.True(quote.IsSuccess, quote.Error); Assert.Equal(25, quote.Value!.EligibleAmount);
        var sender = new Mock<ISender>(MockBehavior.Strict);
        var sync = new SyncOfflineCollectionsCommandHandler(sender.Object, Mock.Of<ISyncRepository>(), f.User, monthly: f.Workflow);
        var operation = new SyncOfflineOperationDto(Guid.NewGuid(), OfflineOperationKind.NpmDaily, f.BusinessDate,
            StallId: f.Stall.Id, ReceivedAmount: quote.Value.EligibleAmount, NpmQuoteToken: quote.Value.Token,
            NpmInstallmentDate: new(2025, 8, 26));
        var first = Assert.Single((await sync.Handle(new([operation]), default)).Value!.Results);
        var replay = Assert.Single((await sync.Handle(new([operation]), default)).Value!.Results);
        Assert.Equal(SyncResultStatus.Synced, first.Status); Assert.Equal(first.CollectionId, replay.CollectionId);
        Assert.Equal(first.ReferenceCode, replay.ReferenceCode);
        Assert.Single(f.Db.Collections); Assert.Equal(25, f.Db.CollectionAllocations.Sum(x => x.Amount));
        Assert.Equal(25, f.Db.DailyCollections.Count()); sender.VerifyNoOtherCalls();
        var stale = operation with { ClientOperationId = Guid.NewGuid() };
        Assert.Equal(SyncResultStatus.ReconciliationRequired,
            Assert.Single((await sync.Handle(new([stale]), default)).Value!.Results).Status);
    }

    [Fact]
    public async Task Legacy_offline_payload_for_activated_month_requires_reconciliation_without_dispatch_or_cash()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(asCollector: true);
        var sender = new Mock<ISender>(MockBehavior.Strict);
        var sync = new SyncOfflineCollectionsCommandHandler(sender.Object, Mock.Of<ISyncRepository>(), f.User, monthly: f.Workflow);
        var result = await sync.Handle(new([new(Guid.NewGuid(), OfflineOperationKind.NpmDaily, f.Month.PeriodStart, StallId: f.Stall.Id)]), default);
        Assert.Equal(SyncResultStatus.ReconciliationRequired, Assert.Single(result.Value!.Results).Status);
        Assert.Empty(f.Db.Collections); Assert.Empty(f.Db.PostingOperations); sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Pre_cutover_online_callback_cannot_settle_activated_month_through_legacy_day_rows()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var settlement = new OnlinePaymentSettlementService(Mock.Of<IPaymentRepository>(), Mock.Of<IStallRepository>(),
            new Mock<INpmMonthSettlementService>(MockBehavior.Strict).Object, Mock.Of<IUtilityBillRepository>(),
            Mock.Of<IOnlinePaymentNotifier>(), uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, f.Workflow);
        var transaction = OnlinePaymentTransaction.CreateForNpmMonth("fixture", Guid.NewGuid(), f.Stall.Id, 2025, 8, 900, "fixture");
        var evt = new PaymentGatewayEvent(PaymentGatewayEventType.Paid, "fixture", 900, "payment-fixture", "fixture", DateTime.UtcNow, "{}");
        var result = await settlement.SettleAsync(transaction, evt);
        Assert.False(result.IsSuccess); Assert.StartsWith("RECONCILIATION_REQUIRED:", result.Error);
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, transaction.Status);
        Assert.Equal(evt.PaymentId, transaction.PaymentId);
        Assert.Equal(evt.Method, transaction.Method);
        Assert.Equal(evt.RawPayload, transaction.RawPayload);
        Assert.Empty(f.Db.Collections); Assert.Empty(f.Db.DailyCollections);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.VerifyNoOtherCalls();
    }
}
