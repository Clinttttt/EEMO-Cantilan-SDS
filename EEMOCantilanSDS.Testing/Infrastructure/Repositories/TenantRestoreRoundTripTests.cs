using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// GOLD-STANDARD verification for the scoped per-municipality restore, run against a REAL PostgreSQL
/// (the raw SQL / json_populate_recordset path cannot run on EF's in-memory provider). OPT-IN: it only
/// runs when the env var KIRO_PG_RESTORE_TEST holds a throwaway Postgres connection string, so CI (which
/// never sets it) skips it. It proves: (1) a snapshot→mutate→restore round-trip reproduces the tenant's
/// data EXACTLY, and (2) a second municipality's rows are left completely untouched.
/// </summary>
public class TenantRestoreRoundTripTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("KIRO_PG_RESTORE_TEST");

    private sealed class FixedMuni(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static DbContextOptions<AppDbContext> Opts() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Conn).Options;

    private static ICurrentUserService User()
    {
        var m = new Mock<ICurrentUserService>();
        m.SetupGet(x => x.Username).Returns("head.test");
        m.SetupGet(x => x.Role).Returns("SuperAdmin");
        m.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        return m.Object;
    }

    private static async Task SeedAsync(Guid mid, Action<AppDbContext> add)
    {
        await using var ctx = new AppDbContext(Opts(), new FixedMuni(mid));
        add(ctx);
        // Stamp the tenant column on every added row (the auto-stamp interceptor isn't wired in a
        // hand-built context), mirroring how the other repository tests seed tenant data.
        foreach (var e in ctx.ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
            if (e.Metadata.FindProperty("MunicipalityId") is not null)
                e.Property("MunicipalityId").CurrentValue = mid;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Restore_RoundTripsTenantExactly_AndLeavesOtherTenantUntouched()
    {
        if (string.IsNullOrWhiteSpace(Conn))
            return;   // opt-in local verification only; skipped in CI

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Fresh schema on the throwaway DB.
        await using (var mig = new AppDbContext(Opts(), new FixedMuni(Guid.Empty)))
        {
            await mig.Database.EnsureDeletedAsync();
            await mig.Database.MigrateAsync();
        }

        // Seed municipality A: facility → stall → contract + a paid daily collection.
        Guid aStallId = Guid.Empty;
        await SeedAsync(a, ctx =>
        {
            var f = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
            var s = Stall.Create(f.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
            aStallId = s.Id;
            var c = Contract.Create(s.Id, "Vendor A", "Vendor A", new DateOnly(2026, 1, 1), 3, 900m);
            var dc = DailyCollection.Create(s.Id, new DateOnly(2026, 1, 5));
            dc.MarkPaid("OR-A-1", collectorId: null);
            ctx.AddRange(f, s, c, dc);
        });

        // Seed municipality B: facility → stall (the "must stay untouched" tenant).
        await SeedAsync(b, ctx =>
        {
            var f = Facility.Create(FacilityCode.NPM, "B Public Market", "NPM");
            var s = Stall.Create(f.Id, "9", 500m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
            ctx.AddRange(f, s);
        });

        // Snapshot A.
        var snapshot = await new TenantRestoreRepository(new AppDbContext(Opts(), new FixedMuni(a)), User())
            .CreateSnapshotAsync(CancellationToken.None);

        // Mutate A destructively: change the stall's rate, delete the daily collection, add a stray stall.
        await using (var mut = new AppDbContext(Opts(), new FixedMuni(a)))
        {
            var stall = await mut.Stalls.FirstAsync(x => x.Id == aStallId);
            mut.Entry(stall).Property("MonthlyRate").CurrentValue = 9999m;
            mut.DailyCollections.RemoveRange(mut.DailyCollections);
            var stray = Stall.Create((await mut.Facilities.FirstAsync()).Id, "999", 111m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
            mut.Add(stray);
            mut.Entry(stray).Property("MunicipalityId").CurrentValue = a;
            await mut.SaveChangesAsync();
        }

        // Restore A from the snapshot.
        var result = await new TenantRestoreRepository(new AppDbContext(Opts(), new FixedMuni(a)), User())
            .RestoreAsync(snapshot, CancellationToken.None);
        Assert.True(result.RowsRestored > 0);

        // Assert A is EXACTLY the snapshot state.
        await using (var check = new AppDbContext(Opts(), new FixedMuni(a)))
        {
            Assert.Equal(1, await check.Stalls.CountAsync());                       // stray stall gone
            var stall = await check.Stalls.FirstAsync(x => x.Id == aStallId);
            Assert.Equal(900m, stall.MonthlyRate);                                  // rate reverted
            Assert.Equal(1, await check.DailyCollections.CountAsync());             // deleted collection restored
            var dc = await check.DailyCollections.FirstAsync();
            Assert.Equal("OR-A-1", dc.ORNumber);
            Assert.True(dc.IsPaid);
            Assert.Equal(1, await check.Contracts.CountAsync());
            Assert.Equal(1, await check.Facilities.CountAsync());
        }

        // Assert B is COMPLETELY untouched.
        await using (var checkB = new AppDbContext(Opts(), new FixedMuni(b)))
        {
            Assert.Equal(1, await checkB.Facilities.CountAsync());
            Assert.Equal(1, await checkB.Stalls.CountAsync());
            var s = await checkB.Stalls.FirstAsync();
            Assert.Equal("9", s.StallNo);
            Assert.Equal(500m, s.MonthlyRate);
        }
    }
}
