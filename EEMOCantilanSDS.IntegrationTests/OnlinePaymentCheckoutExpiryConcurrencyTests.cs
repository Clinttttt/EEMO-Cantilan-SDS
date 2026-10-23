using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class OnlinePaymentCheckoutExpiryConcurrencyTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task CheckoutExpiryCompareAndSet_CannotOverwriteCapturedFundsOrAnotherOffice()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var office = Municipality.Create("TEST-EXPIRY", "Expiry Test Office", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: "test-expiry");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(office);
            await setup.SaveChangesAsync();
        }

        var captured = OnlinePaymentTransaction.CreateForNpmMonth(
            "EEMO-OP-EXPIRY-PAID", Guid.NewGuid(), Guid.NewGuid(), 2026, 10, 2400m, "PayMongo");
        captured.SetPending("cs_paid_concurrently", "https://checkout.paymongo.com/test");
        var abandoned = OnlinePaymentTransaction.CreateForNpmMonth(
            "EEMO-OP-EXPIRY-OPEN", Guid.NewGuid(), Guid.NewGuid(), 2026, 10, 2400m, "PayMongo");
        abandoned.SetPending("cs_verified_expired", "https://checkout.paymongo.com/test");

        await using (var seed = db.CreateContext(office.Id))
        {
            seed.OnlinePaymentTransactions.AddRange(captured, abandoned);
            await seed.SaveChangesAsync();
        }

        // A Head reads an unpaid checkout. Before provider verification finishes,
        // a webhook persists the captured payment in another DbContext.
        await using var head = db.CreateContext(office.Id);
        var stale = await head.OnlinePaymentTransactions.SingleAsync(t => t.Id == captured.Id);
        Assert.Equal(OnlinePaymentStatus.Pending, stale.Status);
        await using (var webhook = db.CreateContext(office.Id))
        {
            var payment = await webhook.OnlinePaymentTransactions.SingleAsync(t => t.Id == captured.Id);
            payment.MarkPaid("pay_real", "qrph", DateTime.UtcNow, "{}");
            await webhook.SaveChangesAsync();
        }

        var repository = new OnlinePaymentRepository(head);
        const string evidence = "{\"status\":\"expired\"}";
        Assert.False(await repository.TryExpireUnsettledCheckoutAsync(
            office.Id, captured.Id, "cs_paid_concurrently", evidence));
        Assert.False(await repository.TryExpireUnsettledCheckoutAsync(
            Guid.NewGuid(), abandoned.Id, "cs_verified_expired", evidence));
        Assert.False(await repository.TryExpireUnsettledCheckoutAsync(
            office.Id, abandoned.Id, "cs_wrong_checkout", evidence));
        Assert.True(await repository.TryExpireUnsettledCheckoutAsync(
            office.Id, abandoned.Id, "cs_verified_expired", evidence));
        Assert.False(await repository.TryExpireUnsettledCheckoutAsync(
            office.Id, abandoned.Id, "cs_verified_expired", evidence));

        await using var verify = db.CreateContext(office.Id);
        var paid = await verify.OnlinePaymentTransactions.SingleAsync(t => t.Id == captured.Id);
        var expired = await verify.OnlinePaymentTransactions.SingleAsync(t => t.Id == abandoned.Id);
        Assert.Equal(OnlinePaymentStatus.Paid, paid.Status);
        Assert.Equal("pay_real", paid.PaymentId);
        Assert.Equal(OnlinePaymentStatus.Expired, expired.Status);
        using var persistedEvidence = System.Text.Json.JsonDocument.Parse(expired.RawPayload!);
        Assert.Equal("expired", persistedEvidence.RootElement.GetProperty("status").GetString());
        Assert.Equal(2400m, paid.Amount);
        Assert.Equal(2400m, expired.Amount);
    }
}
