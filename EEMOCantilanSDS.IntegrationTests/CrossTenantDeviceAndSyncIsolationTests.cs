using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ST-11 findings 6 and 7. A client operation id or a device token that belongs to one LGU must be invisible
/// to, and untouchable by, another LGU - even though both are globally unique values.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CrossTenantDeviceAndSyncIsolationTests(PostgresFixture db)
{
    private async Task<Guid> SeedMunicipalityAsync(string code)
    {
        var m = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.Add(m);
        await setup.SaveChangesAsync();
        return m.Id;
    }

    [SkippableFact]
    public async Task AnotherLgusOperationId_IsNotReportedAsAlreadySynced()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var a = await SeedMunicipalityAsync("SYNC-A");
        var b = await SeedMunicipalityAsync("SYNC-B");
        var op = Guid.NewGuid();

        var facility = Facility.Create(FacilityCode.NPM, "NPM", "NPM", municipalityId: a);
        await using (var write = db.CreateContext(a))
        {
            write.Facilities.Add(facility);
            await write.SaveChangesAsync();
            var stall = Stall.Create(facility.Id, "1", 100m, ApplicableFees.BaseRental, municipalityId: a);
            write.Stalls.Add(stall);
            var day = DailyCollection.Create(stall.Id, new DateOnly(2026, 10, 1), "test", 30m);
            day.SetClientOperationId(op);
            write.DailyCollections.Add(day);
            await write.SaveChangesAsync();
        }

        await using var asA = db.CreateContext(a);
        await using var asB = db.CreateContext(b);
        Assert.True(await new SyncRepository(asA).IsOperationProcessedAsync(op));
        Assert.False(await new SyncRepository(asB).IsOperationProcessedAsync(op));
    }

    [SkippableFact]
    public async Task ADeviceTokenHeldByAnotherLgu_IsNotTakenOver_AndNotRemovedByIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var a = await SeedMunicipalityAsync("TOK-A");
        var b = await SeedMunicipalityAsync("TOK-B");
        var collectorA = Guid.NewGuid();
        var collectorB = Guid.NewGuid();
        var token = $"fcm-{Guid.NewGuid():N}";

        await using (var ctx = db.CreateContext(a))
            Assert.True(await new CollectorDeviceTokenRepository(ctx).UpsertAsync(collectorA, token, "android", a));

        await using (var ctx = db.CreateContext(b))
        {
            var repo = new CollectorDeviceTokenRepository(ctx);
            Assert.False(await repo.UpsertAsync(collectorB, token, "android", b));   // not re-pointed
            await repo.RemoveByTokenAsync(collectorB, token);                         // not removed
        }

        await using var check = db.CreateContext(a);
        var row = await check.CollectorDeviceTokens.SingleAsync(t => t.Token == token);
        Assert.Equal((collectorA, a), (row.CollectorId, row.MunicipalityId));
    }

    [SkippableFact]
    public async Task ADeviceHandedToAnotherCollectorOfTheSameLgu_IsRePointed_AndOnlyTheOwnerCanRemoveIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var a = await SeedMunicipalityAsync("TOK-H");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var token = $"fcm-{Guid.NewGuid():N}";

        await using var ctx = db.CreateContext(a);
        var repo = new CollectorDeviceTokenRepository(ctx);
        Assert.True(await repo.UpsertAsync(first, token, "android", a));
        Assert.True(await repo.UpsertAsync(second, token, "android", a));            // same device, new collector
        await repo.RemoveByTokenAsync(first, token);                                   // the previous owner cannot drop it
        Assert.Single(await repo.GetByCollectorAsync(second));
        await repo.RemoveByTokenAsync(second, token);
        Assert.Empty(await repo.GetByCollectorAsync(second));
    }
}
