using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.OnlinePayments;

public class OnlinePaymentReconcilerTests
{
    private const string Checkout = "cs_open";
    private static readonly DateTime Now = new(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Young = TimeSpan.FromHours(2);
    private static readonly TimeSpan DayOld = TimeSpan.FromHours(24);

    private readonly Mock<IOnlinePaymentRepository> _transactions = new();
    private readonly Mock<IPaymentGateway> _gateway = new();
    private readonly Mock<IPayMongoCheckoutExpiryService> _expiry = new();
    private readonly Mock<IOnlinePaymentSettlementService> _settlement = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private static Result<PaymentGatewayEvent> Event(PaymentGatewayEventType type) =>
        Result<PaymentGatewayEvent>.Success(new PaymentGatewayEvent(type, Checkout, 100m, "pay_1", "qrph", Now, "{}"));

    private OnlinePaymentTransaction Open(TimeSpan age)
    {
        var transaction = OnlinePaymentTransaction.Create("REF", Guid.NewGuid(), Guid.NewGuid(), 100m, "PayMongo");
        transaction.SetPending(Checkout, "https://checkout");
        typeof(OnlinePaymentTransaction).GetProperty(nameof(OnlinePaymentTransaction.CreatedAt))!.SetValue(transaction, Now - age);
        _transactions.Setup(r => r.GetByIdAsync(transaction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        return transaction;
    }

    private void GatewayReads(Result<PaymentGatewayEvent> read) =>
        _gateway.Setup(g => g.RetrievePaymentStatusAsync(Checkout, It.IsAny<CancellationToken>())).ReturnsAsync(read);

    private void ClosingReturns(Result<PaymentGatewayEvent> result) =>
        _expiry.Setup(e => e.ExpireAndVerifyAsync(Checkout, It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private Task Reconcile(OnlinePaymentTransaction transaction) =>
        new OnlinePaymentReconciler(_transactions.Object, _gateway.Object, _expiry.Object, _settlement.Object,
            _unitOfWork.Object, new FixedClock(Now)).ReconcileAsync(transaction.Id);

    private void VerifyNeverClosed() =>
        _expiry.Verify(e => e.ExpireAndVerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    private void VerifyNothingSaved() =>
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task APaymentWhoseWebhookWasLost_IsSettled()
    {
        var transaction = Open(Young);
        var paid = Event(PaymentGatewayEventType.Paid);
        GatewayReads(paid);

        await Reconcile(transaction);

        _settlement.Verify(s => s.SettleAsync(transaction, paid.Value!, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNeverClosed();
    }

    [Theory]
    [InlineData(PaymentGatewayEventType.Unknown)]
    [InlineData(PaymentGatewayEventType.Failed)]
    public async Task AYoungCheckout_StaysOpen_SoThePayorCanStillPayOrRetry(PaymentGatewayEventType read)
    {
        var transaction = Open(Young);
        GatewayReads(Event(read));

        await Reconcile(transaction);

        Assert.Equal(OnlinePaymentStatus.Pending, transaction.Status);
        VerifyNeverClosed();
        VerifyNothingSaved();
    }

    [Theory]
    [InlineData(PaymentGatewayEventType.Unknown, OnlinePaymentStatus.Expired)]
    [InlineData(PaymentGatewayEventType.Failed, OnlinePaymentStatus.Failed)]
    public async Task ADayOldUnpaidCheckout_IsClosedAtTheGateway_ThenEndsLocally(PaymentGatewayEventType read, OnlinePaymentStatus ends)
    {
        var transaction = Open(DayOld);
        GatewayReads(Event(read));
        ClosingReturns(Event(PaymentGatewayEventType.Expired));

        await Reconcile(transaction);

        Assert.Equal(ends, transaction.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task APaymentFoundWhileClosing_IsSettled_NotExpired()
    {
        var transaction = Open(DayOld);
        GatewayReads(Event(PaymentGatewayEventType.Unknown));
        var paid = Event(PaymentGatewayEventType.Paid);
        ClosingReturns(paid);

        await Reconcile(transaction);

        Assert.Equal(OnlinePaymentStatus.Pending, transaction.Status);
        _settlement.Verify(s => s.SettleAsync(transaction, paid.Value!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACheckoutTheGatewayWillNotClose_StaysOpen()
    {
        var transaction = Open(DayOld);
        GatewayReads(Event(PaymentGatewayEventType.Unknown));
        ClosingReturns(Result<PaymentGatewayEvent>.Failure("unavailable", ResultStatus.UpstreamFailed));

        await Reconcile(transaction);

        Assert.Equal(OnlinePaymentStatus.Pending, transaction.Status);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task AnOngoingPaymentThatStillShowsAfterClosing_StaysOpen()
    {
        var transaction = Open(DayOld);
        GatewayReads(Event(PaymentGatewayEventType.Unknown));
        ClosingReturns(Event(PaymentGatewayEventType.Unknown));

        await Reconcile(transaction);

        Assert.Equal(OnlinePaymentStatus.Pending, transaction.Status);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task ASessionAlreadyExpiredAtTheGateway_EndsLocally()
    {
        var transaction = Open(Young);
        GatewayReads(Event(PaymentGatewayEventType.Expired));

        await Reconcile(transaction);

        Assert.Equal(OnlinePaymentStatus.Expired, transaction.Status);
        VerifyNeverClosed();
    }

    [Fact]
    public async Task AnUnreachableGateway_ChangesNothing()
    {
        var transaction = Open(DayOld);
        GatewayReads(Result<PaymentGatewayEvent>.Failure("unavailable", ResultStatus.UpstreamFailed));

        await Reconcile(transaction);

        Assert.Equal(OnlinePaymentStatus.Pending, transaction.Status);
        VerifyNeverClosed();
        VerifyNothingSaved();
    }

    [Fact]
    public async Task AWebhookThatGotThereFirst_IsLeftAlone()
    {
        var transaction = Open(DayOld);
        transaction.MarkPaid("pay_1", "qrph", Now, "{}");

        await Reconcile(transaction);

        _gateway.Verify(g => g.RetrievePaymentStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
