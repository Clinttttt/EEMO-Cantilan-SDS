using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
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
        return (new OnlinePaymentSettlementService(payRepo.Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), payRepo, uow);
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
}
