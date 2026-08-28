using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Settlement must be idempotent, amount-checked, and — critically — must NEVER overwrite a period that
/// was already settled by another channel (offline collection or a duplicate online transaction). The
/// money is still recorded on the online transaction (for audit/refund), but the ledger record's
/// existing Paid status, OR number, and collector attribution are preserved.
/// </summary>
public class OnlinePaymentSettlementServiceTests
{
    private const decimal Amount = 100m;

    private static (OnlinePaymentSettlementService svc, Mock<IPaymentRepository> payRepo, Mock<IUnitOfWork> uow)
        Build(PaymentRecord record)
    {
        var payRepo = new Mock<IPaymentRepository>();
        payRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var notifier = new Mock<IOnlinePaymentNotifier>();
        var uow = new Mock<IUnitOfWork>();
        return (new OnlinePaymentSettlementService(payRepo.Object, new Mock<IStallRepository>().Object, new Mock<INpmMonthSettlementService>().Object, new Mock<IUtilityBillRepository>().Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), payRepo, uow);
    }

    private static OnlinePaymentTransaction PendingTxn(Guid recordId)
    {
        var txn = OnlinePaymentTransaction.Create("EEMO-OP-TEST", Guid.NewGuid(), recordId, Amount, "PayMongo");
        txn.SetPending("cs_1", "https://checkout");
        return txn;
    }

    private static PaymentGatewayEvent PaidEvent(decimal amount = Amount) =>
        new(PaymentGatewayEventType.Paid, "cs_1", amount, "pay_1", "qrph", DateTime.UtcNow, "{}");

    [Fact]
    public async Task OutstandingRecord_IsClearedOnline()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);   // Unpaid
        var (svc, payRepo, _) = Build(record);
        var txn = PendingTxn(record.Id);

        var result = await svc.SettleAsync(txn, PaidEvent());

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, record.Status);
        Assert.Null(record.ORNumber);                    // online OR stays null until staff encode
        payRepo.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadyPaidByAnotherChannel_RecordsTxn_ButPreservesOriginalAttribution()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);
        record.RecordPayment("OR-OFFLINE-123", collectorId: Guid.NewGuid(), PaymentStatus.Paid);   // settled offline
        var (svc, payRepo, _) = Build(record);
        var txn = PendingTxn(record.Id);

        var result = await svc.SettleAsync(txn, PaidEvent());

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);   // money is still recorded (for refund/audit)
        // Original offline attribution is NOT overwritten.
        Assert.Equal("OR-OFFLINE-123", record.ORNumber);
        Assert.NotNull(record.CollectorId);
        Assert.Equal(PaymentStatus.Paid, record.Status);
        payRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AlreadySettledTransaction_IsNoOp()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);
        var (svc, payRepo, _) = Build(record);
        var txn = PendingTxn(record.Id);
        txn.MarkPaid("pay_x", "qrph", DateTime.UtcNow, "{}");   // already settled

        var result = await svc.SettleAsync(txn, PaidEvent());

        Assert.True(result.IsSuccess);
        payRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        payRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AmountMismatch_DoesNotSettle()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);
        var (svc, payRepo, _) = Build(record);
        var txn = PendingTxn(record.Id);

        var result = await svc.SettleAsync(txn, PaidEvent(amount: 50m));   // underpaid

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.NotEqual(OnlinePaymentStatus.Paid, txn.Status);
        payRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LatePaid_AfterExpiry_StillSettles()
    {
        // Webhook delivery is not ordered: an 'expired' event can arrive before the 'paid' event. A
        // confirmed payment must still be recorded (the payor's money was captured).
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);   // Unpaid
        var (svc, payRepo, _) = Build(record);
        var txn = PendingTxn(record.Id);
        txn.MarkExpired("{}");                                                 // provider expiry arrived first

        var result = await svc.SettleAsync(txn, PaidEvent());

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);                    // recovered from Expired
        Assert.Equal(PaymentStatus.Paid, record.Status);
        payRepo.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Paid_AfterFailedAttempt_StillSettles()
    {
        // A payor may fail one attempt then retry and succeed on the same checkout session.
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);
        var (svc, _, _) = Build(record);
        var txn = PendingTxn(record.Id);
        txn.MarkFailed("{}");                                                  // first attempt failed

        var result = await svc.SettleAsync(txn, PaidEvent());                  // retry succeeded

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, record.Status);
    }

    [Fact]
    public async Task BillGrewAfterCheckout_RecordsPartial_NotFullPaid()
    {
        // Captured amount was frozen at 100 when checkout opened; the balance is now 150 (a charge was
        // added). The 100 must NOT clear the full 150 — record a partial of what was actually received.
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, 150m);   // TotalBill 150, Unpaid
        var (svc, payRepo, _) = Build(record);
        var txn = PendingTxn(record.Id);                                    // Amount = 100

        var result = await svc.SettleAsync(txn, PaidEvent());               // evt 100 == txn 100

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);                 // money still recorded on the txn
        Assert.Equal(PaymentStatus.Partial, record.Status);                 // NOT fully Paid
        Assert.Equal(100m, record.PartialAmount);
        payRepo.Verify(r => r.UpdateAsync(record, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NpmTransaction_SettlesMonthDays_AndMarksPaid_WithoutTouchingMonthlyRecord()
    {
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var txn = OnlinePaymentTransaction.CreateForNpmMonth("EEMO-OP-NPM", Guid.NewGuid(), stall.Id, 2026, 6, 150m, "PayMongo");
        txn.SetPending("cs_npm", "https://checkout");

        var payRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var npm = new Mock<INpmMonthSettlementService>();
        npm.Setup(s => s.SettleUnpaidDaysAsync(stall, 2026, 6, null, "Online", It.IsAny<CancellationToken>(), It.IsAny<decimal?>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        var notifier = new Mock<IOnlinePaymentNotifier>();
        var uow = new Mock<IUnitOfWork>();

        var svc = new OnlinePaymentSettlementService(
            payRepo.Object, stallRepo.Object, npm.Object, new Mock<IUtilityBillRepository>().Object, notifier.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var evt = new PaymentGatewayEvent(PaymentGatewayEventType.Paid, "cs_npm", 150m, "pay_npm", "qrph", DateTime.UtcNow, "{}");
        var result = await svc.SettleAsync(txn, evt);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        npm.Verify(s => s.SettleUnpaidDaysAsync(stall, 2026, 6, null, "Online", It.IsAny<CancellationToken>(), It.IsAny<decimal?>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // The monthly-record path is never used for an NPM transaction.
        payRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        payRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NpmUtilityTransaction_MarksBillPaid_WithBlankOr()
    {
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var bill = UtilityBill.Create(stall.Id, 2026, 6, 0m, 10m, 12m, 0m, 5m, 20m);   // elec 120 + water 100 = 220 due
        var txn = OnlinePaymentTransaction.CreateForNpmUtility("EEMO-OP-UTIL", Guid.NewGuid(), stall.Id, 2026, 6, 220m, "PayMongo");
        txn.SetPending("cs_util", "https://checkout");

        var payRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var npm = new Mock<INpmMonthSettlementService>();
        var util = new Mock<IUtilityBillRepository>();
        util.Setup(u => u.GetByStallAndMonthAsync(stall.Id, 2026, 6, It.IsAny<CancellationToken>())).ReturnsAsync(bill);
        var notifier = new Mock<IOnlinePaymentNotifier>();
        var uow = new Mock<IUnitOfWork>();

        var svc = new OnlinePaymentSettlementService(
            payRepo.Object, stallRepo.Object, npm.Object, util.Object, notifier.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var evt = new PaymentGatewayEvent(PaymentGatewayEventType.Paid, "cs_util", 220m, "pay_util", "qrph", DateTime.UtcNow, "{}");
        var result = await svc.SettleAsync(txn, evt);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, bill.Status);   // both electricity + water marked Paid
        Assert.Null(bill.ElecORNumber);                  // OR stays blank until staff encode
        Assert.Null(bill.WaterORNumber);
        payRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NpmUtilityTransaction_BalanceGrewAfterCheckout_RecordsPartial_NotFullPaid()
    {
        // Bill is 220 (elec 120 + water 100) but the captured amount was only 100 (readings edited up while
        // the checkout was open). Settlement must credit exactly ₱100 (elec first) as PARTIAL — never mark
        // the full 220 Paid.
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var bill = UtilityBill.Create(stall.Id, 2026, 6, 0m, 10m, 12m, 0m, 5m, 20m);   // elec 120 + water 100 = 220
        var txn = OnlinePaymentTransaction.CreateForNpmUtility("EEMO-OP-UTIL2", Guid.NewGuid(), stall.Id, 2026, 6, 100m, "PayMongo");
        txn.SetPending("cs_util2", "https://checkout");

        var payRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var npm = new Mock<INpmMonthSettlementService>();
        var util = new Mock<IUtilityBillRepository>();
        util.Setup(u => u.GetByStallAndMonthAsync(stall.Id, 2026, 6, It.IsAny<CancellationToken>())).ReturnsAsync(bill);
        var notifier = new Mock<IOnlinePaymentNotifier>();
        var uow = new Mock<IUnitOfWork>();

        var svc = new OnlinePaymentSettlementService(
            payRepo.Object, stallRepo.Object, npm.Object, util.Object, notifier.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var evt = new PaymentGatewayEvent(PaymentGatewayEventType.Paid, "cs_util2", 100m, "pay_util2", "qrph", DateTime.UtcNow, "{}");
        var result = await svc.SettleAsync(txn, evt);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Partial, bill.Status);    // NOT fully Paid — bill grew beyond the captured amount
        Assert.Equal(100m, bill.AmountPaid);                 // exactly what was captured (elec first)
        Assert.Equal(PaymentStatus.Partial, bill.ElecStatus);
        Assert.Equal(PaymentStatus.Unpaid, bill.WaterStatus);
    }

    [Fact]
    public async Task NpmFishDayTransaction_SettlesThatOneDay_WithDeclaredKilos_WithoutTouchingMonthlyRecord()
    {
        var stall = Stall.Create(Guid.NewGuid(), "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        // Pay ONE day (2026-06-15), declaring 54 kg → ₱30 + 54 = ₱84.
        var txn = OnlinePaymentTransaction.CreateForNpmFishDay("EEMO-OP-FISH", Guid.NewGuid(), stall.Id, 2026, 6, 15, 54m, 84m, "PayMongo");
        txn.SetPending("cs_fish", "https://checkout");

        var payRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var npm = new Mock<INpmMonthSettlementService>();
        npm.Setup(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, "Online", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        var notifier = new Mock<IOnlinePaymentNotifier>();
        var uow = new Mock<IUnitOfWork>();

        var svc = new OnlinePaymentSettlementService(
            payRepo.Object, stallRepo.Object, npm.Object, new Mock<IUtilityBillRepository>().Object, notifier.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var evt = new PaymentGatewayEvent(PaymentGatewayEventType.Paid, "cs_fish", 84m, "pay_fish", "qrph", DateTime.UtcNow, "{}");
        var result = await svc.SettleAsync(txn, evt);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        npm.Verify(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, "Online", It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // The monthly-record path is never used for an NPM fish-day transaction.
        payRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        payRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NpmFishDaysTransaction_SettlesEachDay_WithItsOwnDeclaredKilos()
    {
        // The weighing fee is what makes a fish day cost what it costs, so a payment covering several days has to carry
        // each day's own kilos through to settlement. Settled as one figure, the office would be marking three days with
        // a weight nobody declared, and the day-by-day record it reconciles against would be wrong for two of them.
        var stall = Stall.Create(Guid.NewGuid(), "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);

        var declarations = new[]
        {
            new NpmFishDayDeclarations.Declaration(26, 12.5m),
            new NpmFishDayDeclarations.Declaration(27, 0m),
            new NpmFishDayDeclarations.Declaration(28, 3m),
        };
        // ₱30 a day, ₱1 a kilo: (30 + 12.5) + (30 + 0) + (30 + 3) = ₱105.50
        var txn = OnlinePaymentTransaction.CreateForNpmFishDays(
            "EEMO-OP-FISHDAYS", Guid.NewGuid(), stall.Id, 2026, 8, declarations, 105.50m, "PayMongo");
        txn.SetPending("cs_fishdays", "https://checkout");

        var payRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var npm = new Mock<INpmMonthSettlementService>();
        npm.Setup(s => s.SettleFishDayAsync(stall, It.IsAny<DateOnly>(), It.IsAny<decimal>(), "Online", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        var uow = new Mock<IUnitOfWork>();

        var svc = new OnlinePaymentSettlementService(
            payRepo.Object, stallRepo.Object, npm.Object, new Mock<IUtilityBillRepository>().Object,
            new Mock<IOnlinePaymentNotifier>().Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var evt = new PaymentGatewayEvent(PaymentGatewayEventType.Paid, "cs_fishdays", 105.50m, "pay_fishdays", "gcash", DateTime.UtcNow, "{}");
        var result = await svc.SettleAsync(txn, evt);

        Assert.True(result.IsSuccess);
        npm.Verify(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 8, 26), 12.5m, "Online", It.IsAny<CancellationToken>()), Times.Once);
        npm.Verify(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 8, 27), 0m, "Online", It.IsAny<CancellationToken>()), Times.Once);
        npm.Verify(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 8, 28), 3m, "Online", It.IsAny<CancellationToken>()), Times.Once);
        npm.Verify(s => s.SettleFishDayAsync(It.IsAny<Stall>(), It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        // One unit of work for all three days: the office's register gains them together or not at all.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // And the monthly-record path is never touched, exactly as for a single fish day.
        payRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NpmFishDaysTransaction_DoesNotUseTheDailyMonthPath()
    {
        // Its days are priced from their own kilos, so the amount-capped whole-month settlement must never be reached:
        // that path records no kilos at all and would settle whichever days the money happened to cover.
        var stall = Stall.Create(Guid.NewGuid(), "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var txn = OnlinePaymentTransaction.CreateForNpmFishDays(
            "EEMO-OP-FISHDAYS2", Guid.NewGuid(), stall.Id, 2026, 8,
            new[] { new NpmFishDayDeclarations.Declaration(26, 1m), new NpmFishDayDeclarations.Declaration(27, 2m) },
            63m, "PayMongo");
        txn.SetPending("cs_fishdays2", "https://checkout");

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var npm = new Mock<INpmMonthSettlementService>();
        npm.Setup(s => s.SettleFishDayAsync(It.IsAny<Stall>(), It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);

        var svc = new OnlinePaymentSettlementService(
            new Mock<IPaymentRepository>().Object, stallRepo.Object, npm.Object, new Mock<IUtilityBillRepository>().Object,
            new Mock<IOnlinePaymentNotifier>().Object, new Mock<IUnitOfWork>().Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        await svc.SettleAsync(txn, new PaymentGatewayEvent(
            PaymentGatewayEventType.Paid, "cs_fishdays2", 63m, "pay", "gcash", DateTime.UtcNow, "{}"));

        npm.Verify(s => s.SettleUnpaidDaysAsync(
            It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<decimal?>()), Times.Never);
    }
}
