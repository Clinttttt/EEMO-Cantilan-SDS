using EEMOCantilanSDS.Application.Command.Municipalities.CloseAbandonedTestCheckouts;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class AbandonedCheckoutSafetyTests
{
    private static readonly Guid OfficeId = Guid.NewGuid();

    private static OnlinePaymentTransaction Checkout(Guid municipality, string sessionId)
    {
        var transaction = OnlinePaymentTransaction.Create(
            "EEMO-OP-TEST", Guid.NewGuid(), Guid.NewGuid(), 2400m, "PayMongo");
        typeof(OnlinePaymentTransaction).GetProperty(nameof(OnlinePaymentTransaction.MunicipalityId))!
            .SetValue(transaction, municipality);
        transaction.SetPending(sessionId, "https://checkout.paymongo.com/any");
        return transaction;
    }

    private static async Task<(AppDbContext db, CloseAbandonedTestCheckoutsCommandHandler handler,
        Mock<IPayMongoCheckoutExpiryService> expire)> Setup(
            string merchantKey = "sk_test_office",
            bool head = true,
            OnlinePaymentTransaction? transaction = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        if (transaction is not null)
        {
            db.OnlinePaymentTransactions.Add(transaction);
            await db.SaveChangesAsync();
        }

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.MunicipalityId).Returns(OfficeId);
        user.SetupGet(x => x.Role).Returns(head ? "SuperAdmin" : "Collector");
        var credentials = new Mock<IPayMongoCredentialResolver>();
        credentials.Setup(x => x.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayMongoCredentials(merchantKey, null, null));
        var expire = new Mock<IPayMongoCheckoutExpiryService>();
        var repository = new Mock<IOnlinePaymentRepository>();
        var reads = new OnlinePaymentRepository(db);
        repository.Setup(x => x.HasCapturedReconciliationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken ct) => reads.HasCapturedReconciliationAsync(id, ct));
        repository.Setup(x => x.GetUnsettledMerchantCheckoutsAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, int limit, CancellationToken ct) =>
                reads.GetUnsettledMerchantCheckoutsAsync(id, limit, ct));
        // InMemory does not support ExecuteUpdate. Simulate its conditional update here;
        // the actual PostgreSQL compare-and-set is verified separately.
        repository.Setup(x => x.TryExpireUnsettledCheckoutAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, Guid transactionId, string reference, string evidence, CancellationToken ct) =>
            {
                var row = await db.OnlinePaymentTransactions.FirstOrDefaultAsync(t =>
                    t.MunicipalityId == id && t.Id == transactionId && t.GatewayReference == reference
                    && (t.Status == OnlinePaymentStatus.Initiated || t.Status == OnlinePaymentStatus.Pending), ct);
                if (row is null) return false;
                row.MarkExpired(evidence);
                await db.SaveChangesAsync(ct);
                return true;
            });
        return (db, new CloseAbandonedTestCheckoutsCommandHandler(
            repository.Object, user.Object, credentials.Object, expire.Object), expire);
    }

    [Fact]
    public async Task ProviderConfirmedExpiredTestCheckout_ReleasesOnlyItsOwnUnpaidAttempt()
    {
        const string id = "cs_one";
        var (db, handler, expiry) = await Setup(transaction: Checkout(OfficeId, id));
        await using var dispose = db;
        expiry.Setup(x => x.ExpireAndVerifyAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EEMOCantilanSDS.Application.Common.Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(PaymentGatewayEventType.Expired, id, 0m, null, null, null, "{}")));

        var outcome = await handler.Handle(new(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, outcome.Value!.Expired);
        Assert.Equal(0, outcome.Value.Blocked);
        Assert.Equal(OnlinePaymentStatus.Expired,
            (await db.OnlinePaymentTransactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task ProviderStillActive_PreservesUnpaidRecordAndMerchantRotationBlocker()
    {
        const string id = "cs_open";
        var (db, handler, expiry) = await Setup(transaction: Checkout(OfficeId, id));
        await using var dispose = db;
        expiry.Setup(x => x.ExpireAndVerifyAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EEMOCantilanSDS.Application.Common.Result<PaymentGatewayEvent>.Success(
                new PaymentGatewayEvent(PaymentGatewayEventType.Unknown, id, 0m, null, null, null, "{}")));

        var outcome = await handler.Handle(new(), CancellationToken.None);

        Assert.Equal(0, outcome.Value!.Expired);
        Assert.Equal(1, outcome.Value.Blocked);
        Assert.Equal(OnlinePaymentStatus.Pending,
            (await db.OnlinePaymentTransactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task LiveMerchantAccount_CannotExpireLiveCheckout()
    {
        var (db, handler, expiry) = await Setup(merchantKey: "sk_live_office",
            transaction: Checkout(OfficeId, "cs_live"));
        await using var dispose = db;
        var outcome = await handler.Handle(new(), CancellationToken.None);
        Assert.False(outcome.IsSuccess);
        expiry.Verify(x => x.ExpireAndVerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NonHeadAccount_CannotInvokeFinancialMaintenance()
    {
        var (db, handler, expiry) = await Setup(head: false,
            transaction: Checkout(OfficeId, "cs_open"));
        await using var dispose = db;
        var outcome = await handler.Handle(new(), CancellationToken.None);
        Assert.False(outcome.IsSuccess);
        expiry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckoutOwnedByAnotherMunicipality_IsNotTouched()
    {
        var (db, handler, expiry) = await Setup(transaction: Checkout(Guid.NewGuid(), "cs_other"));
        await using var dispose = db;
        var outcome = await handler.Handle(new(), CancellationToken.None);
        Assert.Equal(0, outcome.Value!.Expired);
        Assert.Equal(OnlinePaymentStatus.Pending,
            (await db.OnlinePaymentTransactions.SingleAsync()).Status);
        expiry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CapturedBetweenReviewAndExpiry_IsCountedBlockedAndNeverRewritten()
    {
        const string id = "cs_paid_during_review";
        var (db, handler, expiry) = await Setup(transaction: Checkout(OfficeId, id));
        await using var dispose = db;
        expiry.Setup(x => x.ExpireAndVerifyAsync(id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var checkout = await db.OnlinePaymentTransactions.SingleAsync();
                checkout.MarkPaid("pay_captured", "qrph", DateTime.UtcNow, "{}");
                await db.SaveChangesAsync();
                return EEMOCantilanSDS.Application.Common.Result<PaymentGatewayEvent>.Success(
                    new PaymentGatewayEvent(PaymentGatewayEventType.Expired, id, 0m, null, null, null, "{}"));
            });

        var result = await handler.Handle(new(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Expired);
        Assert.Equal(1, result.Value.Blocked);
        var saved = await db.OnlinePaymentTransactions.SingleAsync();
        Assert.Equal(OnlinePaymentStatus.Paid, saved.Status);
        Assert.Equal("pay_captured", saved.PaymentId);
    }

    [Fact]
    public async Task CapturedPaymentNeedingReconciliation_PreventsCleanup()
    {
        var pending = Checkout(OfficeId, "cs_paid");
        pending.MarkPaid("pay_success", "qrph", DateTime.UtcNow, "{}");
        pending.MarkSettlementReconciliationRequired("Needs treasury review");
        var (db, handler, expiry) = await Setup(transaction: pending);
        await using var dispose = db;
        var outcome = await handler.Handle(new(), CancellationToken.None);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired,
            (await db.OnlinePaymentTransactions.SingleAsync()).Status);
        expiry.VerifyNoOtherCalls();
    }
}
