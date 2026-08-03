using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// What the collector's field app asks the database for: this month's utility bills and anything older still
/// owed. The rule itself is unit-tested; what only a real PostgreSQL can answer is whether the query we send
/// selects those rows — the month predicate, and the tenant filter that must keep another LGU's arrears out.
///
/// <para>Runs against a throwaway container (see <see cref="PostgresFixture"/>). Skips, stating why, when there
/// is no container runtime.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UtilityBillOutstandingQueryTests(PostgresFixture db)
{
    private static UtilityBill Bill(Guid stallId, int year, int month, decimal waterCurrent) =>
        UtilityBill.Create(stallId, year, month,
            elecPreviousReading: 0m, elecCurrentReading: 0m, elecRatePerKwh: 0m,
            waterPreviousReading: 0m, waterCurrentReading: waterCurrent, waterRatePerCubicMeter: 1m);

    /// <summary>Seeds one LGU with one metered market stall and returns both ids.</summary>
    private async Task<(Guid MunicipalityId, Guid StallId)> SeedStallAsync(string code, string name)
    {
        // Distinct tenant code as well as distinct name: the schema holds them unique, which is itself part of
        // what a real database tells us and an in-memory provider would not.
        var municipality = Municipality.Create(code, name, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.BaseRental | ApplicableFees.Water,
            section: MarketSection.VegetableArea, municipalityId: municipality.Id);

        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(facility);
            tenant.Stalls.Add(stall);
            await tenant.SaveChangesAsync();
        }

        return (municipality.Id, stall.Id);
    }

    [SkippableFact]
    public async Task TheMonthsBills_AndOlderArrears_ComeBack_ButNotOneAlreadyPaid()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, stallId) = await SeedStallAsync("SDS-A", "Municipality A");

        var june = Bill(stallId, 2026, 6, 40m);     // ₱40 still owed
        var july = Bill(stallId, 2026, 7, 50m);     // settled
        july.RecordPayment(
            elecOrNumber: null, waterOrNumber: "OR-JULY", collectorId: null,
            elecStatus: PaymentStatus.Unpaid, elecPartialAmount: null,
            waterStatus: PaymentStatus.Paid, waterPartialAmount: null);
        var august = Bill(stallId, 2026, 8, 56m);   // the month being collected
        var september = Bill(stallId, 2026, 9, 60m); // not yet due

        await using (var tenant = db.CreateContext(municipalityId))
        {
            tenant.UtilityBills.AddRange(june, july, august, september);
            await tenant.SaveChangesAsync();
        }

        await using var read = db.CreateContext(municipalityId);
        var bills = await new UtilityBillRepository(read).GetForMonthWithOutstandingAsync(2026, 8);

        Assert.Equal(2, bills.Count);
        Assert.Equal(6, bills[0].BillingMonth);     // oldest owed first — the one to settle
        Assert.Equal(8, bills[1].BillingMonth);
        Assert.DoesNotContain(bills, b => b.BillingMonth == 7);   // paid, so not offered again
        Assert.DoesNotContain(bills, b => b.BillingMonth == 9);   // a later month is never brought forward
    }

    [SkippableFact]
    public async Task TheChargeAndBalance_SurviveTheRoundTrip()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, stallId) = await SeedStallAsync("SDS-B", "Municipality B");

        // The reported case: 56 cu.m at ₱1.00 on a stall metered for water only.
        await using (var tenant = db.CreateContext(municipalityId))
        {
            tenant.UtilityBills.Add(Bill(stallId, 2026, 8, 56m));
            await tenant.SaveChangesAsync();
        }

        await using var read = db.CreateContext(municipalityId);
        var bill = Assert.Single(await new UtilityBillRepository(read).GetForMonthWithOutstandingAsync(2026, 8));

        Assert.Equal(56m, bill.WaterCharge);        // decimal, not a rounded double
        Assert.Equal(0m, bill.ElecCharge);
        Assert.Equal(56m, bill.BalanceDue);
    }

    [SkippableFact]
    public async Task AnotherLguArrears_AreNotVisible()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var mine = await SeedStallAsync("SDS-C", "Municipality C");
        var theirs = await SeedStallAsync("SDS-D", "Municipality D");

        await using (var a = db.CreateContext(mine.MunicipalityId))
        {
            a.UtilityBills.Add(Bill(mine.StallId, 2026, 8, 56m));
            await a.SaveChangesAsync();
        }
        await using (var b = db.CreateContext(theirs.MunicipalityId))
        {
            b.UtilityBills.Add(Bill(theirs.StallId, 2026, 6, 999m));   // their unpaid arrears
            await b.SaveChangesAsync();
        }

        await using var read = db.CreateContext(mine.MunicipalityId);
        var bills = await new UtilityBillRepository(read).GetForMonthWithOutstandingAsync(2026, 8);

        var only = Assert.Single(bills);
        Assert.Equal(56m, only.WaterCharge);
        Assert.All(bills, x => Assert.Equal(mine.MunicipalityId, x.MunicipalityId));
    }

    [SkippableFact]
    public async Task TheSchema_MatchesTheMigrations()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        await using var read = db.CreateContext(Guid.Empty);

        // Applied cleanly by the fixture, and nothing the model expects is still pending — a migration that does
        // not build the schema the code reads is a deployment failure, and this is where it surfaces.
        Assert.Empty(await read.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await read.Database.GetAppliedMigrationsAsync());
    }
}
