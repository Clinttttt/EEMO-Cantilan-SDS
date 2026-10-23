using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WeighingRateVersionPostgresTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task Fresh_migration_preserves_legacy_history_and_isolates_future_fish_and_meat_rates()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var fishOffice = Municipality.Create("FISH-TEST", "Fish Test Municipality", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: "fish-test");
        var otherOffice = Municipality.Create("MEAT-TEST", "Meat Test Municipality", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: "meat-test");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.AddRange(fishOffice, otherOffice);
            await setup.SaveChangesAsync();
        }

        var before = new DateOnly(2026, 10, 23);
        var after = before.AddDays(1);
        await using (var seed = db.CreateContext(fishOffice.Id))
        {
            seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM,
                FeeRateKey.NpmMeatPerKilo, 66m, new DateOnly(2026, 1, 1), fishOffice.Id));
            seed.WeighingRateVersions.AddRange(
                WeighingRateVersion.Create(fishOffice.Id, FishMeatVendorType.Fish,
                    true, 2.50m, after, "Head Test"),
                WeighingRateVersion.Create(fishOffice.Id, FishMeatVendorType.Meat,
                    false, null, after, "Head Test"));
            await seed.SaveChangesAsync();
        }
        await using (var seed = db.CreateContext(otherOffice.Id))
        {
            seed.WeighingRateVersions.Add(
                WeighingRateVersion.Create(otherOffice.Id, FishMeatVendorType.Meat,
                    true, 60m, after, "Other Head"));
            await seed.SaveChangesAsync();
        }

        await using var fishDb = db.CreateContext(fishOffice.Id);
        var fishRates = new FeeRateResolver(fishDb);
        Assert.Equal(66m, (await WeighingRateSchedule.ResolveAsync(fishDb, fishRates,
            fishOffice.Id, FishMeatVendorType.Meat, before, default))?.RatePerKilo);
        Assert.Null(await WeighingRateSchedule.ResolveAsync(fishDb, fishRates,
            fishOffice.Id, FishMeatVendorType.Meat, after, default));
        var fish = await WeighingRateSchedule.ResolveAsync(fishDb, fishRates,
            fishOffice.Id, FishMeatVendorType.Fish, after, default);
        Assert.NotNull(fish);
        Assert.Equal(2.50m, fish.RatePerKilo);
        Assert.False(fish.Legacy);
        Assert.True(await WeighingRateSchedule.HasAvailableTypeAsync(fishDb, fishOffice.Id, after, default));
        Assert.Equal(2, await fishDb.WeighingRateVersions.CountAsync());

        await using var meatDb = db.CreateContext(otherOffice.Id);
        var meatRates = new FeeRateResolver(meatDb);
        Assert.Null(await WeighingRateSchedule.ResolveAsync(meatDb, meatRates,
            otherOffice.Id, FishMeatVendorType.Fish, after, default));
        Assert.Equal(60m, (await WeighingRateSchedule.ResolveAsync(meatDb, meatRates,
            otherOffice.Id, FishMeatVendorType.Meat, after, default))?.RatePerKilo);
        Assert.Single(await meatDb.WeighingRateVersions.ToListAsync());
        Assert.Empty(await db.CreateContext(Guid.Empty).WeighingRateVersions.ToListAsync());
    }

    [SkippableFact]
    public async Task Database_rejects_an_invalid_disabled_row_despite_a_valid_entity()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var office = Municipality.Create("W-RATE-CONSTRAINT", "Constraint Test Municipality",
            "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "w-rate-constraint");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(office);
            await setup.SaveChangesAsync();
        }
        await using var tenant = db.CreateContext(office.Id);
        var valid = WeighingRateVersion.Create(office.Id, FishMeatVendorType.Fish,
            false, null, new DateOnly(2026, 10, 24), "Head");
        tenant.WeighingRateVersions.Add(valid);
        await tenant.SaveChangesAsync();

        var violation = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await tenant.WeighingRateVersions.Where(x => x.Id == valid.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RatePerKilo, 1m));
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
    }
}
