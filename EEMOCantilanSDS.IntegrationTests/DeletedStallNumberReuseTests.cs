using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Soft deletion hides an old stall identity without deleting its history. The PostgreSQL
/// unique indexes must use the same live-only scope as EF queries and stall registration.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeletedStallNumberReuseTests(PostgresFixture database)
{
    [SkippableFact]
    public async Task Deleted_meat_stall_number_is_reusable_while_history_and_live_uniqueness_remain()
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "PostgreSQL unavailable");
        await database.ResetAsync();
        var municipality = Municipality.Create("STALE-NPM", "Stall number lifecycle", "Test", MunicipalityStatus.Active);
        await using var db = database.CreateContext(municipality.Id);
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: municipality.Id);
        db.Municipalities.Add(municipality);
        db.Facilities.Add(facility);
        var old = Stall.Create(facility.Id, "11", 900m, ApplicableFees.BaseRental,
            section: MarketSection.MeatSection, municipalityId: municipality.Id);
        db.Stalls.Add(old);
        await db.SaveChangesAsync();

        old.SoftDelete("test-head");
        await db.SaveChangesAsync();

        var replacement = Stall.Create(facility.Id, "11", 900m, ApplicableFees.BaseRental,
            section: MarketSection.MeatSection, municipalityId: municipality.Id);
        db.Stalls.Add(replacement);
        await db.SaveChangesAsync();

        var live = await db.Stalls.AsNoTracking().Where(x => x.FacilityId == facility.Id
            && x.Section == MarketSection.MeatSection && x.StallNo == "11").ToListAsync();
        Assert.Equal(replacement.Id, Assert.Single(live).Id);

        var history = await db.Stalls.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.MunicipalityId == municipality.Id && x.FacilityId == facility.Id
                && x.Section == MarketSection.MeatSection && x.StallNo == "11").ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, x => x.Id == old.Id && x.IsDeleted);
        Assert.Contains(history, x => x.Id == replacement.Id && !x.IsDeleted);

        // A second CURRENT stall is still prohibited by the database even if a writer bypasses
        // the application-level validator. This is distinct from reuse of a soft-deleted number.
        var duplicate = Stall.Create(facility.Id, "11", 900m, ApplicableFees.BaseRental,
            section: MarketSection.MeatSection, municipalityId: municipality.Id);
        db.Stalls.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Deleted_custom_section_stall_reuses_number_only_within_its_section()
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "PostgreSQL unavailable");
        await database.ResetAsync();
        var municipality = Municipality.Create("STALE-CUSTOM", "Custom stall lifecycle", "Test", MunicipalityStatus.Active);
        await using var db = database.CreateContext(municipality.Id);
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: municipality.Id);
        db.Municipalities.Add(municipality);
        db.Facilities.Add(facility);
        var original = Stall.Create(facility.Id, "11", 900m, ApplicableFees.BaseRental,
            section: null, customSectionName: "Section A", municipalityId: municipality.Id);
        db.Stalls.Add(original);
        await db.SaveChangesAsync();
        original.SoftDelete("test-head");
        await db.SaveChangesAsync();

        var replacement = Stall.Create(facility.Id, "11", 900m, ApplicableFees.BaseRental,
            section: null, customSectionName: "Section A", municipalityId: municipality.Id);
        var otherSection = Stall.Create(facility.Id, "11", 900m, ApplicableFees.BaseRental,
            section: null, customSectionName: "Section B", municipalityId: municipality.Id);
        db.Stalls.AddRange(replacement, otherSection);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Stalls.AsNoTracking().CountAsync(x => x.FacilityId == facility.Id && x.StallNo == "11"));
        Assert.Equal(3, await db.Stalls.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.MunicipalityId == municipality.Id && x.FacilityId == facility.Id && x.StallNo == "11"));
    }
}
