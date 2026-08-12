using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// One LGU cannot read another's rows, proven against a real PostgreSQL.
///
/// <para>
/// The rule is unit-tested against the in-memory provider, which shares the filter expression but not the SQL. What only a
/// real database can answer is whether the predicate EF composes actually reaches the server and selects what we think —
/// and this is the boundary where being wrong means one municipality reading another's collections. The architecture review
/// asked for exactly this test and it did not exist.
/// </para>
///
/// <para>Runs against a throwaway container (see <see cref="PostgresFixture"/>). Skips, stating why, when there is no
/// container runtime.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class TenantIsolationTests(PostgresFixture db)
{
    /// <summary>Seeds one LGU with one facility, both stamped to that LGU, and returns its id.</summary>
    private async Task<Guid> SeedLguAsync(string code, string facilityName)
    {
        var municipality = Municipality.Create(
            code, code, "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());

        // Municipality is not tenant-owned, so setup writes it with no tenant resolved.
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        // The facility IS tenant-owned. Written as that tenant, so the interceptor stamps it exactly as production would.
        await using (var asTenant = db.CreateContext(municipality.Id))
        {
            asTenant.Facilities.Add(Facility.Create(FacilityCode.TCC, facilityName, "TCC"));
            await asTenant.SaveChangesAsync();
        }

        return municipality.Id;
    }

    [SkippableFact]
    public async Task ATenantReadsItsOwnRowsAndNotAnothers()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var lguA = await SeedLguAsync("ISO-A", "A's Commercial Center");
        var lguB = await SeedLguAsync("ISO-B", "B's Commercial Center");

        await using (var asA = db.CreateContext(lguA))
        {
            var seen = await asA.Facilities.ToListAsync();
            Assert.Single(seen);
            Assert.Equal("A's Commercial Center", seen[0].Name);
        }

        await using (var asB = db.CreateContext(lguB))
        {
            var seen = await asB.Facilities.ToListAsync();
            Assert.Single(seen);
            Assert.Equal("B's Commercial Center", seen[0].Name);
        }
    }

    [SkippableFact]
    public async Task AnUnresolvedTenantReadsNothing()
    {
        // The fail-closed half. Before it, this returned BOTH LGUs' rows - the worst possible answer to a request whose
        // tenant could not be established.
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        await SeedLguAsync("ISO-C", "C's Commercial Center");
        await SeedLguAsync("ISO-D", "D's Commercial Center");

        await using var unresolved = db.CreateContext(Guid.Empty);

        Assert.Empty(await unresolved.Facilities.ToListAsync());

        // And the rows are really there - this is a filter, not an empty database.
        Assert.Equal(2, await unresolved.Facilities.IgnoreQueryFilters().CountAsync());
    }

    [SkippableFact]
    public async Task ATenantCannotReachAnothersRowByIdEither()
    {
        // A filtered list is the obvious case; a direct lookup by primary key is the one that gets forgotten, and it is
        // how a crafted request would try: an id from one LGU submitted by another.
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var lguA = await SeedLguAsync("ISO-E", "E's Commercial Center");
        var lguB = await SeedLguAsync("ISO-F", "F's Commercial Center");

        Guid aFacilityId;
        await using (var asA = db.CreateContext(lguA))
            aFacilityId = (await asA.Facilities.SingleAsync()).Id;

        await using var asB = db.CreateContext(lguB);
        Assert.Null(await asB.Facilities.FirstOrDefaultAsync(f => f.Id == aFacilityId));
    }

    [SkippableFact]
    public async Task AWriteIsStampedWithTheWritersTenant()
    {
        // The other half of isolation: a row must land in the tenant that wrote it, or it would be invisible to them and
        // visible to nobody. The stamp is an interceptor, so only a real save through the pipeline shows it.
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        var lguA = await SeedLguAsync("ISO-G", "G's Commercial Center");

        await using var unresolved = db.CreateContext(Guid.Empty);
        var written = await unresolved.Facilities.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(lguA, written.MunicipalityId);
    }
}
