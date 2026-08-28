using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// An online payment covering several fish days, as the database holds it and as the office's own screens find it.
///
/// <para>
/// Two things only a real PostgreSQL can answer. First, that the declared kilos survive the round trip: they are stored
/// as one text column of day:kilos pairs, and a payment settles from what is READ BACK, not from what was in memory when
/// the payor tapped. Second, that the awaiting-OR query returns such a payment. That query is SQL with a subquery over
/// the daily collections, and a payment missing from it is a payment the office can never receipt — invisible rather
/// than wrong, which is worse.
/// </para>
///
/// <para>Runs against a throwaway container (see <see cref="PostgresFixture"/>). Skips, stating why, when there is no
/// container runtime.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class OnlineFishDaysPaymentQueryTests(PostgresFixture db)
{
    /// <summary>Seeds one LGU with a fish-section market stall and a payor, and returns what the tests need.</summary>
    private async Task<(Guid MunicipalityId, Guid StallId, Guid PayorId)> SeedAsync(string code, string name)
    {
        var municipality = Municipality.Create(code, name, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental,
            section: MarketSection.FishSection, municipalityId: municipality.Id);
        var payor = PayorUser.Create("Godon Lar", "09384326778", new HashedPassword("AQAAAAIAAYagAAAAEExampleHashForTests=="));

        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(facility);
            tenant.Stalls.Add(stall);
            tenant.PayorUsers.Add(payor);
            await tenant.SaveChangesAsync();
        }

        return (municipality.Id, stall.Id, payor.Id);
    }

    [SkippableFact]
    public async Task TheDeclaredKilosSurviveTheRoundTrip_DayByDay()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, stallId, payorId) = await SeedAsync("SDS-F1", "Fish Municipality One");

        var declarations = new[]
        {
            new NpmFishDayDeclarations.Declaration(26, 12.5m),
            new NpmFishDayDeclarations.Declaration(27, 0m),
            new NpmFishDayDeclarations.Declaration(28, 3m),
        };

        await using (var write = db.CreateContext(municipalityId))
        {
            write.OnlinePaymentTransactions.Add(OnlinePaymentTransaction.CreateForNpmFishDays(
                "EEMO-OP-20260828-FISHDAYS", payorId, stallId, 2026, 8, declarations, 105.50m, "PayMongo"));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(municipalityId);
        var stored = await read.OnlinePaymentTransactions
            .SingleAsync(t => t.Reference == "EEMO-OP-20260828-FISHDAYS");

        Assert.Equal(OnlinePaymentTargetKind.NpmFishDays, stored.TargetKind);
        Assert.Equal("26:12.5,27:0,28:3", stored.FishDayDeclarations);
        Assert.Equal(105.50m, stored.Amount);          // numeric(18,2), not a rounded double

        // And read back as the days settlement will mark, each with its own weight.
        var days = stored.FishDays();
        Assert.Equal(3, days.Count);
        Assert.Equal(new NpmFishDayDeclarations.Declaration(26, 12.5m), days[0]);
        Assert.Equal(new NpmFishDayDeclarations.Declaration(27, 0m), days[1]);
        Assert.Equal(new NpmFishDayDeclarations.Declaration(28, 3m), days[2]);

        // The single-day fields stay empty: this payment belongs to no one day.
        Assert.Null(stored.TargetDay);
        Assert.Null(stored.DeclaredFishKilos);
    }

    [SkippableFact]
    public async Task ThePaymentReachesTheOfficesAwaitingReceiptQueue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var (municipalityId, stallId, payorId) = await SeedAsync("SDS-F2", "Fish Municipality Two");

        var transaction = OnlinePaymentTransaction.CreateForNpmFishDays(
            "EEMO-OP-20260828-QUEUE", payorId, stallId, 2026, 8,
            new[] { new NpmFishDayDeclarations.Declaration(26, 1m), new NpmFishDayDeclarations.Declaration(27, 2m) },
            63m, "PayMongo");
        transaction.SetPending("cs_queue", "https://checkout/queue");
        transaction.MarkPaid("pay_queue", "gcash", DateTime.UtcNow, "{}");

        await using (var write = db.CreateContext(municipalityId))
        {
            write.OnlinePaymentTransactions.Add(transaction);

            // Settlement's own effect: each day paid, blank OR, awaiting the office's receipt.
            foreach (var (day, kilos) in new[] { (26, 1m), (27, 2m) })
            {
                var dc = DailyCollection.Create(stallId, new DateOnly(2026, 8, day), "Online", 30m);
                dc.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: kilos, updatedBy: "Online");
                write.DailyCollections.Add(dc);
            }

            await write.SaveChangesAsync();
        }

        await using (var read = db.CreateContext(municipalityId))
        {
            var awaiting = await new OnlinePaymentRepository(read).GetAwaitingOrByPeriodAsync(2026, 8);

            var row = Assert.Single(awaiting);
            Assert.Equal("EEMO-OP-20260828-QUEUE", row.Reference);
            Assert.Equal(FacilityCode.NPM, row.Facility);
            Assert.Equal("1", row.StallNo);
            Assert.Equal("Godon Lar", row.PayorName);
            Assert.Equal(63m, row.Amount);
            Assert.Equal("2026-08", row.Period);
        }

        // Once the office has stamped its receipt on those days, the payment leaves the queue rather than sitting there
        // for ever.
        await using (var stamp = db.CreateContext(municipalityId))
        {
            foreach (var dc in await stamp.DailyCollections.ToListAsync())
                dc.SetOrNumber("OR-12345", "Admin");
            await stamp.SaveChangesAsync();
        }

        await using (var after = db.CreateContext(municipalityId))
        {
            Assert.Empty(await new OnlinePaymentRepository(after).GetAwaitingOrByPeriodAsync(2026, 8));
        }
    }
}
