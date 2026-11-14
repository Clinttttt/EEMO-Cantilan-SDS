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

        var settlement = new OnlinePaymentSettlementService(paymentRepo.Object, new Mock<IStallRepository>().Object, new Mock<INpmMonthSettlementService>().Object, new Mock<IUtilityBillRepository>().Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

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
    public async Task Paid_AmountMismatch_IsReportedAsUnderReview_NotAsCredited()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount - 1m));   // underpayment

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Settled);
        Assert.Equal("UnderReview", result.Value.Status);
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, txn.Status);
        Assert.Equal(PaymentStatus.Unpaid, record.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACaptureAlreadyInReconciliation_IsNeverReportedAsCredited()
    {
        // IsSettled is true for this state (it only means "do not settle again"), and the portal used to tell the payor
        // their account had been credited while the balance still showed as owing.
        var (txn, record) = PendingPair();
        txn.MarkPaid("pay_x", "gcash", DateTime.UtcNow, "{}");
        txn.MarkSettlementReconciliationRequired("needs treasury review");
        var (handler, uow, gateway, _) = Build(txn, record, evt: null);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Settled);
        Assert.Equal("UnderReview", result.Value.Status);
        gateway.Verify(g => g.RetrievePaymentStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADecidedCapture_ReportsWhatTheHeadDecided()
    {
        var (refunded, record) = PendingPair();
        refunded.MarkPaid("pay_x", "gcash", DateTime.UtcNow, "{}");
        refunded.MarkSettlementReconciliationRequired("duplicate");
        refunded.RecordOfficeDecision(OnlinePaymentDisposition.Refunded, "ref_1", null, "head", DateTime.UtcNow);
        var (handler, _, _, _) = Build(refunded, record, evt: null);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal("Refunded", result.Value!.Status);
        Assert.False(result.Value.Settled);

        var (applied, appliedRecord) = PendingPair();
        applied.MarkPaid("pay_y", "gcash", DateTime.UtcNow, "{}");
        applied.MarkSettlementReconciliationRequired("mismatch");
        applied.RecordOfficeDecision(OnlinePaymentDisposition.Applied, "OR-1", null, "head", DateTime.UtcNow);
        var (appliedHandler, _, _, _) = Build(applied, appliedRecord, evt: null);

        var appliedResult = await appliedHandler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal("Paid", appliedResult.Value!.Status);
        Assert.True(appliedResult.Value.Settled);
    }

    [Theory]
    [InlineData(OnlinePaymentStatus.Failed)]
    [InlineData(OnlinePaymentStatus.Expired)]
    public async Task AFailedOrExpiredRow_IsAskedAboutAgain_SoALateCaptureIsNotLost(OnlinePaymentStatus status)
    {
        // A payor can retry after a failed attempt, and a missed webhook leaves only this path to find the money.
        var (txn, record) = PendingPair();
        if (status == OnlinePaymentStatus.Failed) txn.MarkFailed("{}"); else txn.MarkExpired("{}");
        var (handler, _, gateway, _) = Build(txn, record, PaidEvent(Amount));

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.Value!.Settled);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, record.Status);
        gateway.Verify(g => g.RetrievePaymentStatusAsync(GatewayRef, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailedRowWithNothingPaidAtTheGateway_StaysFailed()
    {
        var (txn, record) = PendingPair();
        txn.MarkFailed("{}");
        var (handler, _, _, _) = Build(txn, record, PendingEvent());

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal("Failed", result.Value!.Status);
        Assert.False(result.Value.Settled);
        Assert.Equal(OnlinePaymentStatus.Failed, txn.Status);
    }

    [Fact]
    public async Task LosingTheRaceToAWebhook_DoesNotClaimPaidBeforeReadingTheOutcome()
    {
        // The winner may have stored a reconciliation rather than a settlement, so the next confirm has to read it.
        var (txn, _) = PendingPair();
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(g => g.RetrievePaymentStatusAsync(GatewayRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentGatewayEvent>.Success(PaidEvent(Amount)));
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.GetByReferenceAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(txn);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Role).Returns("Payor");
        currentUser.SetupGet(c => c.UserId).Returns(txn.PayorUserId);
        var settlement = new Mock<IOnlinePaymentSettlementService>();
        settlement.Setup(s => s.SettleAsync(txn, It.IsAny<PaymentGatewayEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());
        var handler = new ConfirmOnlinePaymentCommandHandler(
            onlineRepo.Object, gateway.Object, settlement.Object, currentUser.Object, new Mock<IUnitOfWork>().Object);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Settled);
        Assert.Equal("Pending", result.Value.Status);
    }

    [Fact]
    public async Task NonOwningPayor_IsForbidden_DoesNotReverify()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, gateway, _) = Build(txn, record, PaidEvent(Amount), role: "Payor", userId: Guid.NewGuid());

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
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
    public async Task AFailedAttempt_IsReported_ButTheCheckoutStaysOpenForARetry()
    {
        // Marking the row Failed would let the next payment open a second session while this one can still take money.
        var (txn, record) = PendingPair();
        var failed = new PaymentGatewayEvent(PaymentGatewayEventType.Failed, GatewayRef, 0m, null, null, null, "{}");
        var (handler, uow, _, _) = Build(txn, record, failed);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal("Failed", result.Value!.Status);
        Assert.False(result.Value.Settled);
        Assert.True(txn.IsResumable);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownReference_IsNotFound()
    {
        var (handler, _, _, _) = Build(txn: null, record: null, evt: null);

        var result = await handler.Handle(new ConfirmOnlinePaymentCommand(Reference), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }
}
