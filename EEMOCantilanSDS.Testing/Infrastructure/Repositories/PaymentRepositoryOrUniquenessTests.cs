using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

public class PaymentRepositoryOrUniquenessTests : RepositoryTestBase
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    // Regression: OR numbers must be unique across modules. A payment OR that already
    // exists on a slaughter receipt must be rejected (previously only Payments+Daily were checked).
    [Fact]
    public async Task IsORNumberUnique_FalseWhenUsedInAnotherModule()
    {
        await using var ctx = NewContext();

        ctx.Add(SlaughterTransaction.CreateHog(
            Guid.NewGuid(), Guid.NewGuid(), "owner", heads: 1, orNumber: "OR-X", transactionDate: new DateOnly(2026, 1, 1)));
        await ctx.SaveChangesAsync();

        var repo = new PaymentRepository(ctx);

        Assert.False(await repo.IsORNumberUniqueAsync("OR-X", CancellationToken.None));
        Assert.True(await repo.IsORNumberUniqueAsync("OR-UNUSED", CancellationToken.None));
    }

    // Phase 5 hardening: OR uniqueness is scoped per municipality — a second LGU may reuse an OR number
    // that only exists in another LGU, but within the same LGU it is still rejected.
    [Fact]
    public async Task IsORNumberUnique_IsScopedPerMunicipality()
    {
        var dbName = Guid.NewGuid().ToString();
        var lguA = Guid.NewGuid();
        var lguB = Guid.NewGuid();

        DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

        // Seed OR-A under LGU-A (stamp the tenant explicitly).
        await using (var seed = new AppDbContext(Options()))
        {
            var tx = SlaughterTransaction.CreateHog(
                Guid.NewGuid(), Guid.NewGuid(), "owner", heads: 1, orNumber: "OR-A", transactionDate: new DateOnly(2026, 1, 1));
            seed.Add(tx);
            seed.Entry(tx).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = lguA;
            await seed.SaveChangesAsync();
        }

        // LGU-B: OR-A is free (belongs to another LGU).
        await using (var ctxB = new AppDbContext(Options(), new FixedMunicipality(lguB)))
        {
            Assert.True(await new PaymentRepository(ctxB).IsORNumberUniqueAsync("OR-A", CancellationToken.None));
        }

        // LGU-A: OR-A is still taken.
        await using (var ctxA = new AppDbContext(Options(), new FixedMunicipality(lguA)))
        {
            Assert.False(await new PaymentRepository(ctxA).IsORNumberUniqueAsync("OR-A", CancellationToken.None));
        }
    }

    // #4 regression — cross-module OR uniqueness now spans utility bills too, both directions.
    [Fact]
    public async Task IsORNumberUnique_FalseWhenUsedOnUtilityBill()
    {
        await using var ctx = NewContext();

        var bill = EEMOCantilanSDS.Domain.Entities.Payments.UtilityBill.Create(
            Guid.NewGuid(), 2026, 7, 0, 10, 10m, 0, 5, 20m, "admin");
        bill.RecordPayment("OR-UTIL", null, null,
            EEMOCantilanSDS.Domain.Enums.PaymentStatus.Paid, null,
            EEMOCantilanSDS.Domain.Enums.PaymentStatus.Unpaid, null, updatedBy: "admin");
        ctx.Add(bill);
        await ctx.SaveChangesAsync();

        // A collection OR must be rejected when it already exists on a utility bill.
        Assert.False(await new PaymentRepository(ctx).IsORNumberUniqueAsync("OR-UTIL", CancellationToken.None));
    }

    [Fact]
    public async Task UtilityBill_IsORNumberUnique_FalseWhenUsedByCollection_ButAllowsRemarkingSameBill()
    {
        await using var ctx = NewContext();

        // An OR used on a slaughter receipt...
        ctx.Add(SlaughterTransaction.CreateHog(
            Guid.NewGuid(), Guid.NewGuid(), "owner", heads: 1, orNumber: "OR-COLL", transactionDate: new DateOnly(2026, 1, 1)));

        // ...and a utility bill that already carries its own OR.
        var bill = EEMOCantilanSDS.Domain.Entities.Payments.UtilityBill.Create(
            Guid.NewGuid(), 2026, 7, 0, 10, 10m, 0, 5, 20m, "admin");
        bill.RecordPayment("OR-SELF", null, null,
            EEMOCantilanSDS.Domain.Enums.PaymentStatus.Paid, null,
            EEMOCantilanSDS.Domain.Enums.PaymentStatus.Unpaid, null, updatedBy: "admin");
        ctx.Add(bill);
        await ctx.SaveChangesAsync();

        var repo = new EEMOCantilanSDS.Infrastructure.Repositories.Payments.UtilityBillRepository(ctx);

        // A utility OR is rejected when it collides with a collection OR (cross-module).
        Assert.False(await repo.IsORNumberUniqueAsync("OR-COLL", null, CancellationToken.None));
        // But re-marking THIS bill with its own OR is still allowed (excluded by id).
        Assert.True(await repo.IsORNumberUniqueAsync("OR-SELF", bill.Id, CancellationToken.None));
        // A fresh OR is available.
        Assert.True(await repo.IsORNumberUniqueAsync("OR-FRESH", null, CancellationToken.None));
    }

    // Broader OR uniqueness: the service-facility checks (SLH/TPM/TRM) now also reject a utility OR.
    [Fact]
    public async Task Slaughter_ORChecks_RejectUtilityOr()
    {
        await using var ctx = NewContext();

        var bill = EEMOCantilanSDS.Domain.Entities.Payments.UtilityBill.Create(
            Guid.NewGuid(), 2026, 7, 0, 10, 10m, 0, 5, 20m, "admin");
        bill.RecordPayment("OR-UTIL", null, null,
            EEMOCantilanSDS.Domain.Enums.PaymentStatus.Paid, null,
            EEMOCantilanSDS.Domain.Enums.PaymentStatus.Unpaid, null, updatedBy: "admin");
        ctx.Add(bill);
        await ctx.SaveChangesAsync();

        var slh = new EEMOCantilanSDS.Infrastructure.Repositories.SlaughterRepository(ctx);

        Assert.False(await slh.IsORNumberUniqueAsync("OR-UTIL", CancellationToken.None));
        Assert.False(await slh.IsORNumberAvailableForReceiptAsync("OR-UTIL", "Owner", new DateOnly(2026, 7, 1), CancellationToken.None));
        Assert.True(await slh.IsORNumberUniqueAsync("OR-FRESH", CancellationToken.None));
    }
}
