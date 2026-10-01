using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The same collector username may exist in two LGUs. A scoped login (the default Cantilan path, or a confirmed binding)
/// must resolve the account inside that municipality and never fall through to another tenant's account.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectorTenantScopedLoginLookupTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task TheSameUsernameInTwoLgus_ResolvesInsideTheScopedMunicipality_Only()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        var cantilan = Municipality.Create($"CAN{Guid.NewGuid():N}"[..10].ToUpperInvariant(), "Cantilan", "Surigao del Sur",
            MunicipalityStatus.Active, isDefault: true, tenantCode: $"can-{Guid.NewGuid():N}"[..20]);
        var madrid = Municipality.Create($"MAD{Guid.NewGuid():N}"[..10].ToUpperInvariant(), "Madrid", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"mad-{Guid.NewGuid():N}"[..20]);
        var inCantilan = CollectorUser.Create("Cantilan Collector", "C-001", "personal1", null, null, new HashedPassword("hash-cantilan"), cantilan.Id);
        var inMadrid = CollectorUser.Create("Madrid Collector", "M-001", "personal1", null, null, new HashedPassword("hash-madrid"), madrid.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(cantilan, madrid, inCantilan, inMadrid);
            await setup.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext(Guid.Empty);
        var repo = new CollectorRepository(ctx);

        var cantilanHit = await repo.GetByUsernameOrEmployeeIdAsync("personal1", cantilan.Id);
        var madridHit = await repo.GetByUsernameOrEmployeeIdAsync("personal1", madrid.Id);
        var nowhere = await repo.GetByUsernameOrEmployeeIdAsync("personal1", Guid.NewGuid());

        Assert.Equal(inCantilan.Id, cantilanHit?.Id);
        Assert.Equal(inMadrid.Id, madridHit?.Id);
        Assert.Null(nowhere);                                   // an unknown scope never falls through globally
    }
}
