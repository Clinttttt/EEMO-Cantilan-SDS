using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The slaughterhouse OR rule, asked of a real PostgreSQL.
///
/// <para>Two animals butchered on one receipt share its OR number, so the check has to tell "the same person, typed
/// differently" from "somebody else". Comparing by person means the predicate now lowercases and trims the stored name, and
/// whether PostgreSQL can EXECUTE that comparison is not something an in-memory provider can answer: it evaluates in LINQ and
/// would pass whether or not the SQL exists. A predicate EF cannot translate throws only when a clerk uses it.</para>
///
/// <para>The tenant filter is exercised at the same time, because an OR is unique within an LGU and must not be blocked by
/// another municipality's receipt.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class SlaughterOwnerMatchingQueryTests(PostgresFixture db)
{
    private static readonly DateOnly Day = new(2026, 6, 9);

    private async Task<(Guid MunicipalityId, Guid FacilityId)> SeedFacilityAsync(string code, string name)
    {
        var municipality = Municipality.Create(code, name, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());

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

        return (municipality.Id, facility.Id);
    }

    [SkippableTheory]
    [InlineData("Alan Cayetano", true)]        // exactly as first entered
    [InlineData("alan cayetano", true)]        // same person, lower case
    [InlineData("ALAN CAYETANO", true)]        // same person, caps
    [InlineData("  Alan  Cayetano ", true)]    // same person, padded and double-spaced
    [InlineData("Donya Laras", false)]         // a different person may not take the OR
    [InlineData("Alan Cayetana", false)]       // one letter apart is a different person
    public async Task TheReceiptsOrIsUsableByTheSamePersonHoweverTheNameWasTyped(string ownerAsTypedAgain, bool expected)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var (municipalityId, facilityId) = await SeedFacilityAsync(
            "SLHM" + Guid.NewGuid().ToString("N")[..6], "Match " + Guid.NewGuid().ToString("N")[..6]);

        await using (var seed = db.CreateContext(municipalityId))
        {
            seed.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(
                facilityId, collectorId: null, ownerName: "Alan Cayetano", heads: 1, orNumber: "OR-SLH-1",
                transactionDate: Day));
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext(municipalityId);
        var repo = new SlaughterRepository(ctx);

        var available = await repo.IsORNumberAvailableForReceiptAsync("OR-SLH-1", ownerAsTypedAgain, Day, CancellationToken.None);

        Assert.Equal(expected, available);
    }

    [SkippableFact]
    public async Task AnotherLguReceiptDoesNotBlockThisLgusOrNumber()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var (firstId, firstFacility) = await SeedFacilityAsync(
            "SLHA" + Guid.NewGuid().ToString("N")[..6], "First " + Guid.NewGuid().ToString("N")[..6]);
        var (secondId, _) = await SeedFacilityAsync(
            "SLHB" + Guid.NewGuid().ToString("N")[..6], "Second " + Guid.NewGuid().ToString("N")[..6]);

        await using (var seed = db.CreateContext(firstId))
        {
            seed.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(
                firstFacility, collectorId: null, ownerName: "Alan Cayetano", heads: 1, orNumber: "OR-SHARED",
                transactionDate: Day));
            await seed.SaveChangesAsync();
        }

        await using var other = db.CreateContext(secondId);
        var repo = new SlaughterRepository(other);

        Assert.True(
            await repo.IsORNumberAvailableForReceiptAsync("OR-SHARED", "Someone Else", Day, CancellationToken.None),
            "an OR is unique within one LGU; another municipality's receipt must not reserve it");
    }

    [SkippableFact]
    public async Task AClientsMonthTotalsEverySpellingOfTheirName()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var (municipalityId, facilityId) = await SeedFacilityAsync(
            "SLHH" + Guid.NewGuid().ToString("N")[..6], "History " + Guid.NewGuid().ToString("N")[..6]);

        // The same client on three market days, entered by three different hands.
        await using (var seed = db.CreateContext(municipalityId))
        {
            seed.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(
                facilityId, null, "Juan Dela Cruz", 1, "OR-H1", Day));
            seed.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(
                facilityId, null, "juan dela cruz", 1, "OR-H2", Day.AddDays(1)));
            seed.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(
                facilityId, null, "JUAN  DELA CRUZ", 1, "OR-H3", Day.AddDays(2)));
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext(municipalityId);
        var repo = new SlaughterRepository(ctx);

        var history = await repo.GetOwnerTransactionHistoryAsync("Juan Dela Cruz", Day.Year, Day.Month, CancellationToken.None);

        Assert.Equal(3, history.TransactionGroups.Sum(g => g.Transactions.Count));
    }
}
