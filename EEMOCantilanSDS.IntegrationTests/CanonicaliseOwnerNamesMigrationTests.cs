using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The data migration that canonicalises stored slaughterhouse owner names.
///
/// <para>Migrations are usually left to the schema tests, but this one WRITES to the office's records, and its statement is
/// raw SQL that no compiler checks. What is asserted here is the statement's own behaviour: that it collapses the whitespace
/// it claims to, that it leaves capitalisation and ordinary names untouched, and that running it twice changes nothing.</para>
///
/// <para>The statement is executed as text, exactly as the migration holds it, rather than re-expressed here — a copy that
/// drifts from the migration would test nothing.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class CanonicaliseOwnerNamesMigrationTests(PostgresFixture db)
{
    /// <summary>The migration's statement, character for character.</summary>
    private const string Statement = @"
                UPDATE ""SlaughterTransactions""
                SET ""OwnerName"" = btrim(regexp_replace(""OwnerName"", '\s+', ' ', 'g'))
                WHERE ""OwnerName"" IS NOT NULL
                  AND ""OwnerName"" <> btrim(regexp_replace(""OwnerName"", '\s+', ' ', 'g'));
            ";

    [SkippableFact]
    public async Task ItCollapsesRedundantWhitespaceAndLeavesEverythingElseAlone()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var municipality = Municipality.Create(
            "MIG" + Guid.NewGuid().ToString("N")[..6], "Mig " + Guid.NewGuid().ToString("N")[..6],
            "Surigao del Sur", MunicipalityStatus.Active, tenantCode: Guid.NewGuid().ToString("N")[..8]);

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH", municipalityId: municipality.Id);
        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(facility);
            await tenant.SaveChangesAsync();
        }

        // Written with raw SQL so the entity's own canonicalisation cannot pre-clean them: these stand for rows that were
        // stored before the rule existed.
        var day = new DateOnly(2026, 6, 9);
        var rows = new (string Or, string Stored, string Expected)[]
        {
            ("MIG-1", "Juan  Dela Cruz",   "Juan Dela Cruz"),     // internal double space
            ("MIG-2", "  Ana Reyes  ",     "Ana Reyes"),          // padded
            ("MIG-3", "JUAN DELA CRUZ",    "JUAN DELA CRUZ"),     // capitalisation is not touched
            ("MIG-4", "Ana Reyes",         "Ana Reyes"),          // already canonical
            ("MIG-5", "Ma.  Luisa   Yap",  "Ma. Luisa Yap"),      // several runs
        };

        await using (var seed = db.CreateContext(municipality.Id))
        {
            foreach (var row in rows)
            {
                await seed.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""SlaughterTransactions""
                        (""Id"", ""MunicipalityId"", ""FacilityId"", ""OwnerName"", ""AnimalType"", ""NumberOfHeads"",
                         ""RatePerHead"", ""ORNumber"", ""TransactionDate"", ""SlaughterFee"", ""AntemortemFee"",
                         ""TableCharge"", ""CreatedAt"", ""CreatedBy"", ""IsDeleted"")
                      VALUES ({0}, {1}, {2}, {3}, 0, 1, 0, {4}, {5}, 0, 0, 0, {6}, 'test', false)",
                    Guid.NewGuid(), municipality.Id, facility.Id, row.Stored, row.Or, day, DateTime.UtcNow);
            }
        }

        await using var ctx = db.CreateContext(municipality.Id);

        var changedFirstRun = await ctx.Database.ExecuteSqlRawAsync(Statement);
        Assert.Equal(3, changedFirstRun);   // MIG-1, MIG-2 and MIG-5 differ; MIG-3 and MIG-4 already canonical

        foreach (var row in rows)
        {
            var stored = await ctx.SlaughterTransactions.AsNoTracking()
                .Where(x => x.ORNumber == row.Or).Select(x => x.OwnerName).SingleAsync();
            Assert.Equal(row.Expected, stored);
        }

        // Idempotent: a second run must touch nothing, so a re-deploy cannot churn the office's records.
        var changedSecondRun = await ctx.Database.ExecuteSqlRawAsync(Statement);
        Assert.Equal(0, changedSecondRun);
    }
}
