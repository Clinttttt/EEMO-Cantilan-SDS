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
}
