using EEMOCantilanSDS.Application.Command.OnlinePayments.Confirm;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.OnlinePayments;

/// <summary>
/// The reconciliation/confirm fallback is the safety net for when a webhook never arrives. It must
/// verify with the provider, settle idempotently, reject amount mismatches, and enforce ownership so a
/// payor can only confirm their own payment (staff may reconcile any).
/// </summary>
public class ConfirmOnlinePaymentCommandHandlerTests
{
    private const string Reference = "EEMO-OP-20260613-ABCD1234";
    private const string GatewayRef = "cs_test_123";
    private const decimal Amount = 2400m;

    private static (OnlinePaymentTransaction txn, PaymentRecord record) PendingPair()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);
        var txn = OnlinePaymentTransaction.Create(Reference, Guid.NewGuid(), record.Id, Amount, "PayMongo");
        txn.SetPending(GatewayRef, "https://checkout.test/cs");
        return (txn, record);
    }

    private static PaymentGatewayEvent PaidEvent(decimal amount) =>
        new(PaymentGatewayEventType.Paid, GatewayRef, amount, "pay_1", "gcash", DateTime.UtcNow, "{\"raw\":true}");

    private static PaymentGatewayEvent PendingEvent() =>
        new(PaymentGatewayEventType.Unknown, GatewayRef, 0m, null, null, null, "{\"raw\":true}");

    private static (ConfirmOnlinePaymentCommandHandler handler, Mock<IUnitOfWork> uow, Mock<IPaymentGateway> gateway, Mock<IOnlinePaymentNotifier> notifier)
        Build(OnlinePaymentTransaction? txn, PaymentRecord? record, PaymentGatewayEvent? evt, string role = "Payor", Guid? userId = null)
    {
        var gateway = new Mock<IPaymentGateway>();
        if (evt is not null)
            gateway.Setup(g => g.RetrievePaymentStatusAsync(GatewayRef, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<PaymentGatewayEvent>.Success(evt));

        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.GetByReferenceAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(txn);

        var paymentRepo = new Mock<IPaymentRepository>();
        if (record is not null)
            paymentRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.UserId).Returns(userId ?? txn?.PayorUserId);

        var notifier = new Mock<IOnlinePaymentNotifier>();
        var uow = new Mock<IUnitOfWork>();

        var settlement = new OnlinePaymentSettlementService(paymentRepo.Object, new Mock<IStallRepository>().Object, new Mock<INpmMonthSettlementService>().Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var handler = new ConfirmOnlinePaymentCommandHandler(
            onlineRepo.Object, gateway.Object, settlement, currentUser.Object, uow.Object);

        return (handler, uow, gateway, notifier);
    }

    [Fact]
    public async Task Paid_AsOwningPayor_Settles_ClearsBalance()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, notifier) = Build(txn, record, PaidEvent(Amount));

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Settled);
        Assert.Equal("Paid", result.Value.Status);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, record.Status);
        Assert.Equal(0m, record.BalanceDue);
        Assert.Null(record.CollectorId);   // online payment carries no collector
        Assert.Null(record.ORNumber);      // OR encoded later by staff
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.NotifyPaymentReceivedAsync(It.IsAny<OnlinePaymentNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadySettled_IsIdempotent_DoesNotReverify()
    {
        var (txn, record) = PendingPair();
        txn.MarkPaid("pay_x", "gcash", DateTime.UtcNow, "{}");   // already settled
        var (handler, uow, gateway, _) = Build(txn, record, evt: null);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Settled);
        gateway.Verify(g => g.RetrievePaymentStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StillPendingAtProvider_IsNoOp()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PendingEvent());

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Settled);
        Assert.Equal("Pending", result.Value.Status);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Paid_AmountMismatch_IsRejected_DoesNotSettle()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount - 1m));   // underpayment

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        Assert.Equal(PaymentStatus.Unpaid, record.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NonOwningPayor_IsForbidden_DoesNotReverify()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, gateway, _) = Build(txn, record, PaidEvent(Amount), role: "Payor", userId: Guid.NewGuid());

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        gateway.Verify(g => g.RetrievePaymentStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Staff_CanReconcileAnyPayorsPayment()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount), role: "Admin", userId: Guid.NewGuid());

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Settled);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownReference_IsNotFound()
    {
        var (handler, _, _, _) = Build(txn: null, record: null, evt: null);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
