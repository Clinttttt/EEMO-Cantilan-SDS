using EEMOCantilanSDS.Application.Command.OnlinePayments.HandleWebhook;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.OnlinePayments;

/// <summary>
/// Webhook handling is the most safety-critical path: it moves money state and clears delinquency.
/// These cover fail-closed signature checks, idempotency, amount integrity, and delinquency clearing.
/// </summary>
public class HandlePaymentWebhookCommandHandlerTests
{
    private const string GatewayRef = "cs_test_123";
    private const decimal Amount = 2400m;

    private static (OnlinePaymentTransaction txn, PaymentRecord record) PendingPair()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, Amount);
        var txn = OnlinePaymentTransaction.Create("EEMO-OP-20260613-ABCD1234", Guid.NewGuid(), record.Id, Amount, "PayMongo");
        txn.SetPending(GatewayRef, "https://checkout.test/cs");
        return (txn, record);
    }

    private static PaymentGatewayEvent PaidEvent(decimal amount) =>
        new(PaymentGatewayEventType.Paid, GatewayRef, amount, "pay_1", "gcash", DateTime.UtcNow, "{\"raw\":true}");

    private static (HandlePaymentWebhookCommandHandler handler, Mock<IUnitOfWork> uow, Mock<IPaymentGateway> gateway, Mock<IOnlinePaymentNotifier> notifier)
        Build(OnlinePaymentTransaction? txn, PaymentRecord? record, PaymentGatewayEvent? evt,
            bool signatureValid = true, bool namedWebhook = false, bool signedForAnotherTenant = false,
            bool isDefault = true, Mock<IUtilityBillRepository>? util = null, Mock<INpmMonthSettlementService>? npm = null, Stall? stall = null)
    {
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(g => g.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>())).Returns(signatureValid);
        gateway.Setup(g => g.ParseEvent(It.IsAny<string>()))
            .Returns(evt is null ? Result<PaymentGatewayEvent>.Failure("bad") : Result<PaymentGatewayEvent>.Success(evt));

        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.GetByGatewayReferenceAsync(GatewayRef, It.IsAny<CancellationToken>())).ReturnsAsync(txn);

        var paymentRepo = new Mock<IPaymentRepository>();
        if (record is not null)
            paymentRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var uow = new Mock<IUnitOfWork>();

        var notifier = new Mock<IOnlinePaymentNotifier>();

        var stallRepo = new Mock<IStallRepository>();
        if (stall is not null)
            stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var settlement = new OnlinePaymentSettlementService(paymentRepo.Object, stallRepo.Object, (npm ?? new Mock<INpmMonthSettlementService>()).Object, (util ?? new Mock<IUtilityBillRepository>()).Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var municipalityRepo = new Mock<IMunicipalityRepository>();
        var owner = Municipality.Create("OWNER", "Owner", "PH", MunicipalityStatus.Active, "owner", isDefault: isDefault);
        if (txn is not null)
            typeof(OnlinePaymentTransaction).GetProperty(nameof(OnlinePaymentTransaction.MunicipalityId))!
                .SetValue(txn, owner.Id);
        if (record is not null)
            typeof(PaymentRecord).GetProperty(nameof(PaymentRecord.MunicipalityId))!
                .SetValue(record, owner.Id);
        municipalityRepo.Setup(r => r.GetByIdAsync(owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(owner);

        var credentialResolver = new Mock<IPayMongoCredentialResolver>();
        if (namedWebhook)
        {
            var signer = signedForAnotherTenant
                ? Municipality.Create("SIGNER", "Signer", "PH", MunicipalityStatus.Active, "signer")
                : owner;
            municipalityRepo.Setup(r => r.GetByIdentifierAsync("webhook-tenant", It.IsAny<CancellationToken>()))
                .ReturnsAsync(signer);
            credentialResolver.Setup(r => r.ResolveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PayMongoCredentials("sk_test_fake", null, "whsk_fake"));
            gateway.Setup(g => g.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>(), "whsk_fake"))
                .Returns(signatureValid);
        }
        var tenantScope = new EEMOCantilanSDS.Infrastructure.Tenancy.RequestTenantScope();

        return (new HandlePaymentWebhookCommandHandler(gateway.Object, onlineRepo.Object, settlement, uow.Object, municipalityRepo.Object, credentialResolver.Object, tenantScope), uow, gateway, notifier);
    }

    [Fact]
    public async Task InvalidSignature_FailsClosed_Unauthorized_AndDoesNotParse()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, gateway, _) = Build(txn, record, PaidEvent(Amount), signatureValid: false);

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "bad-sig"), CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        gateway.Verify(g => g.ParseEvent(It.IsAny<string>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
    }

    [Fact]
    public async Task WebhookSignedForAnotherLguCannotSettleAnUnrelatedLgusTransaction()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, gateway, _) = Build(txn, record, PaidEvent(Amount),
            namedWebhook: true, signedForAnotherTenant: true);
        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig", "webhook-tenant"),
            CancellationToken.None);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        Assert.Equal(PaymentStatus.Unpaid, record.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        gateway.Verify(g => g.VerifyWebhookSignature("{}", "sig", "whsk_fake"), Times.Once);
    }

    [Fact]
    public async Task LegacyWebhookCannotSettleAnotherMunicipalityEvenWithValidDefaultSignature()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount), isDefault: false);
        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"),
            CancellationToken.None);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        Assert.Equal(PaymentStatus.Unpaid, record.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidSameLguWebhookCanSettleOnlyItsOwnTransaction()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount),
            namedWebhook: true, isDefault: false);
        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig", "webhook-tenant"),
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, record.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ST-13: new checkouts for these kinds are refused, but a transaction created before that must still settle via the webhook.
    [Fact]
    public async Task PreExistingNpmUtilityTransaction_StillSettlesThroughTheWebhook()
    {
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var bill = UtilityBill.Create(stall.Id, 2026, 6, 0m, 10m, 12m, 0m, 5m, 20m);   // 220 due
        var txn = OnlinePaymentTransaction.CreateForNpmUtility("EEMO-OP-UTIL", Guid.NewGuid(), stall.Id, 2026, 6, 220m, "PayMongo");
        txn.SetPending(GatewayRef, "https://checkout.test/cs");
        var util = new Mock<IUtilityBillRepository>();
        util.Setup(u => u.GetByStallAndMonthAsync(stall.Id, 2026, 6, It.IsAny<CancellationToken>())).ReturnsAsync(bill);
        var (handler, uow, _, _) = Build(txn, null, PaidEvent(220m), util: util);

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        Assert.Equal(PaymentStatus.Paid, bill.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreExistingNpmFishDayTransaction_StillSettlesThroughTheWebhook()
    {
        var stall = Stall.Create(Guid.NewGuid(), "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var txn = OnlinePaymentTransaction.CreateForNpmFishDay("EEMO-OP-FISH", Guid.NewGuid(), stall.Id, 2026, 6, 15, 54m, 84m, "PayMongo");
        txn.SetPending(GatewayRef, "https://checkout.test/cs");
        var npm = new Mock<INpmMonthSettlementService>();
        npm.Setup(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, "Online", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        var (handler, _, _, _) = Build(txn, null, PaidEvent(84m), npm: npm, stall: stall);

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        npm.Verify(s => s.SettleFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, "Online", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Paid_ClearsBalance_NoCollector_NoOrNumber()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount));

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        // Delinquency clears: the record is fully Paid with zero balance...
        Assert.Equal(PaymentStatus.Paid, record.Status);
        Assert.Equal(0m, record.BalanceDue);
        // ...and online payments carry no collector and no OR until staff encode it.
        Assert.Null(record.CollectorId);
        Assert.Null(record.ORNumber);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Paid_IsIdempotent_SecondEventIsNoOp()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount));

        var first = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);
        var second = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Paid, txn.Status);
        // Only the first event mutates state / saves; the replay is a no-op.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Paid_AmountMismatch_IsRejected_AndDoesNotSettle()
    {
        var (txn, record) = PendingPair();
        var (handler, uow, _, _) = Build(txn, record, PaidEvent(Amount - 1m)); // underpayment

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);   // not settled
        Assert.Equal(PaymentStatus.Unpaid, record.Status);       // balance untouched
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownTransaction_IsAcknowledged_NoOp()
    {
        var (handler, uow, _, _) = Build(txn: null, record: null, PaidEvent(Amount));

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(result.IsSuccess); // ack so the provider stops retrying
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Paid_PublishesNotification_WithStallAndPeriod()
    {
        // The realtime alert must identify which stall/period was paid so admin facility pages can
        // refresh the exact row that flips Unpaid → Paid.
        var (txn, record) = PendingPair();
        var (handler, _, _, notifier) = Build(txn, record, PaidEvent(Amount));

        var result = await handler.Handle(new HandlePaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifier.Verify(n => n.NotifyPaymentReceivedAsync(
            It.Is<OnlinePaymentNotification>(p =>
                p.StallId == record.StallId &&
                p.BillingYear == record.BillingYear &&
                p.BillingMonth == record.BillingMonth &&
                p.Amount == Amount),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
