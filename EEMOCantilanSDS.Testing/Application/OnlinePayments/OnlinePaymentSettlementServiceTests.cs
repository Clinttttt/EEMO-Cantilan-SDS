using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
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
        Assert.Equal(409, result.StatusCode);
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
        npm.Setup(s => s.SettleUnpaidDaysAsync(stall, 2026, 6, null, "Online", It.IsAny<CancellationToken>()))
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
        npm.Verify(s => s.SettleUnpaidDaysAsync(stall, 2026, 6, null, "Online", It.IsAny<CancellationToken>()), Times.Once);
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
}
