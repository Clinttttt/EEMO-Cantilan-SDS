using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class OnlinePaymentOneOpenCheckoutTests(PostgresFixture db)
{
    private const string IndexName = "UX_OnlinePaymentTransactions_OneOpenCheckoutPerRecord";

    private static OnlinePaymentTransaction Open(Guid recordId, string name)
    {
        var transaction = OnlinePaymentTransaction.Create("TEST-" + name, Guid.NewGuid(), recordId, 100m, "PayMongo");
        transaction.SetPending("cs_" + name, "https://checkout.test");
        return transaction;
    }

    private async Task<(Municipality office, PaymentRecord record)> SeedAsync()
    {
        await db.ResetAsync();
        var office = Municipality.Create("TEST-OPEN", "One Open Checkout Office", "Test", MunicipalityStatus.Active, tenantCode: "test-open");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(office);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.NCC, "Test monthly facility", "NCC");
        var stall = Stall.Create(facility.Id, "1", 100m, ApplicableFees.BaseRental);
        var record = PaymentRecord.Create(stall.Id, 2026, 10, 100m);
        await using var context = db.CreateContext(office.Id);
        context.AddRange(facility, stall, record);
        await context.SaveChangesAsync();
        return (office, record);
    }

    private async Task<bool> IndexExistsAsync()
    {
        await using var context = db.CreateContext(Guid.Empty);
        return await context.Database
            .SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE indexname = {IndexName}")
            .AnyAsync();
    }

    [SkippableFact]
    public async Task ASecondOpenCheckout_IsRefused_UntilTheFirstEnds()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var (office, record) = await SeedAsync();
        await using var context = db.CreateContext(office.Id);
        var first = Open(record.Id, "first");
        context.Add(first);
        await context.SaveChangesAsync();

        await using (var racing = db.CreateContext(office.Id))
        {
            racing.Add(Open(record.Id, "racing"));
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => racing.SaveChangesAsync());
            Assert.Contains(IndexName, refused.InnerException?.Message);
        }

        // The way Initiate retires a checkout: the old row is saved first, then the new one is added.
        first.MarkExpired("{}");
        await context.SaveChangesAsync();
        context.Add(Open(record.Id, "second"));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.OnlinePaymentTransactions.CountAsync());
    }

    [SkippableFact]
    public async Task EndedCheckouts_DoNotBlockANewOne()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var (office, record) = await SeedAsync();
        await using var context = db.CreateContext(office.Id);

        var failed = Open(record.Id, "failed");
        failed.MarkFailed("{}");
        var held = Open(record.Id, "held");
        held.MarkPaid("pay_held", "qrph", DateTime.UtcNow, "{}");
        held.MarkSettlementReconciliationRequired("held for the office");
        var paid = Open(record.Id, "paid");
        paid.MarkPaid("pay_paid", "qrph", DateTime.UtcNow, "{}");
        context.AddRange(failed, held, paid, Open(record.Id, "open"));

        await context.SaveChangesAsync();

        Assert.Equal(4, await context.OnlinePaymentTransactions.CountAsync());
    }

    [SkippableFact]
    public async Task ExistingDuplicates_SkipTheIndex_InsteadOfFailingTheRelease()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var (office, record) = await SeedAsync();
        await using var context = db.CreateContext(office.Id);
        await context.Database.ExecuteSqlRawAsync($"DROP INDEX \"{IndexName}\"");
        try
        {
            var first = Open(record.Id, "dup1");
            var second = Open(record.Id, "dup2");
            context.AddRange(first, second);
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync(OnlinePaymentOneOpenCheckout20261104.CreateIndexUnlessDuplicatesExist);
            Assert.False(await IndexExistsAsync());

            second.MarkExpired("{}");
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync(OnlinePaymentOneOpenCheckout20261104.CreateIndexUnlessDuplicatesExist);
            Assert.True(await IndexExistsAsync());
        }
        finally
        {
            // The database is shared by the whole collection, so the index always goes back.
            await db.ResetAsync();
            await context.Database.ExecuteSqlRawAsync(OnlinePaymentOneOpenCheckout20261104.CreateIndexUnlessDuplicatesExist);
        }
    }
}
