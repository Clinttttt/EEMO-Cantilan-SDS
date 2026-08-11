using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Infrastructure.Persistence;

/// <summary>
/// What the municipality query filter and the write stamp DO today, written down before either is changed.
///
/// <para>
/// The architecture review's first priority is that an UNRESOLVED tenant makes the filter a no-op, so every LGU's rows
/// are visible instead of none. That is true, and these tests pin it down — including the paths that RELY on it, which
/// is why the fix cannot be a one-line flip. Startup migrates and seeds before the default municipality is resolved, so
/// during seeding the tenant is unresolved by design; failing closed there would hide the rows a seeder checks for and
/// invite it to create a second Cantilan.
/// </para>
///
/// <para>
/// These are characterization tests. Several assert behaviour that is WRONG for a production multi-tenant boundary. They
/// exist so the change that corrects it has to state which of these it is changing, rather than discovering it in
/// production. Each one that changes should be rewritten in the same commit as the fix, not deleted.
/// </para>
/// </summary>
public class TenantFilterCharacterizationTests
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static DbContextOptions<AppDbContext> SharedStore() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tenant-filter-{Guid.NewGuid()}")
            // The stamp is an interceptor in production, so a context built without it writes unstamped rows whatever
            // the tenant. Registered here or these tests would describe the test rig rather than the system.
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    /// <summary>Two facilities, one per tenant, written with the tenant resolved so each is stamped correctly.</summary>
    private static void SeedOnePerTenant(DbContextOptions<AppDbContext> options)
    {
        using (var a = new AppDbContext(options, new FixedMunicipality(TenantA)))
        {
            a.Add(Facility.Create(FacilityCode.TCC, "A's Commercial Center", "TCC"));
            a.SaveChanges();
        }

        using (var b = new AppDbContext(options, new FixedMunicipality(TenantB)))
        {
            b.Add(Facility.Create(FacilityCode.TCC, "B's Commercial Center", "TCC"));
            b.SaveChanges();
        }
    }

    [Fact]
    public void AResolvedTenantSeesOnlyItsOwnRows()
    {
        // The boundary that matters, and it holds. Everything else here is about what happens when the tenant is NOT
        // resolved.
        var options = SharedStore();
        SeedOnePerTenant(options);

        using var a = new AppDbContext(options, new FixedMunicipality(TenantA));
        var seen = a.Facilities.ToList();

        Assert.Single(seen);
        Assert.Equal("A's Commercial Center", seen[0].Name);
    }

    [Fact]
    public void AnUnresolvedTenantSeesEVERYTHING_theBehaviourUnderReview()
    {
        // CHARACTERIZATION OF A DEFECT. Guid.Empty means "unresolved", and the filter treats it as a no-op, so a context
        // with no resolved tenant reads every LGU's rows. A production request cannot reach this today - it falls back
        // to the default municipality - but nothing in the filter says so, and that is the point.
        var options = SharedStore();
        SeedOnePerTenant(options);

        using var unresolved = new AppDbContext(options, new FixedMunicipality(Guid.Empty));

        Assert.Equal(2, unresolved.Facilities.Count());
    }

    [Fact]
    public void AContextBuiltWithNoAccessorAlsoSeesEverything()
    {
        // The options-only constructor: tooling, migrations and a great many tests. Whatever replaces the no-op has to
        // keep this working or the test suite and the design-time factory go with it.
        var options = SharedStore();
        SeedOnePerTenant(options);

        using var bare = new AppDbContext(options);

        Assert.Equal(2, bare.Facilities.Count());
    }

    [Fact]
    public void AWriteWithAResolvedTenantIsStampedWithIt()
    {
        var options = SharedStore();

        using (var a = new AppDbContext(options, new FixedMunicipality(TenantA)))
        {
            a.Add(Facility.Create(FacilityCode.ICE, "A's Iceplant", "ICE"));
            a.SaveChanges();
        }

        using var bare = new AppDbContext(options);
        var written = bare.Facilities.Single();

        Assert.Equal(TenantA, written.MunicipalityId);
    }

    [Fact]
    public void AWriteWithNoResolvedTenantIsREFUSED_notSilentlyLeftUnstamped()
    {
        // CHANGED BEHAVIOUR, and the first half of the fix.
        //
        // This used to save the row unstamped, so it belonged to NOBODY: invisible to every resolved tenant, visible to
        // every unresolved one, counted by no LGU's reports. Silence was the worst outcome available - the row is
        // written, the office is told it saved, and it is not in their register.
        //
        // Nothing in production writes one: every seeder and the first-admin path resolve the municipality themselves,
        // and production carries zero unstamped rows in every tenant-owned table. So this cannot fire for a working
        // deployment; it fires for a broken one.
        var options = SharedStore();

        using var unresolved = new AppDbContext(options, new FixedMunicipality(Guid.Empty));
        unresolved.Add(Facility.Create(FacilityCode.BBQ, "Nobody's Barbecue Stand", "BBQ"));

        var refused = Assert.Throws<InvalidOperationException>(() => unresolved.SaveChanges());
        Assert.Contains("no municipality", refused.Message);
        Assert.Contains("Facility", refused.Message);

        // And nothing was written.
        using var bare = new AppDbContext(options);
        Assert.Empty(bare.Facilities);
    }

    [Fact]
    public void AWriteFromAContextWithNoAccessorIsStillAllowed()
    {
        // Design-time tooling, migrations and much of the suite build contexts with no accessor and legitimately work
        // across tenants. Refusing those would take the migration story and the test suite with it, so "no accessor" and
        // "accessor that resolved to nothing" are deliberately NOT the same thing.
        var options = SharedStore();

        using var bare = new AppDbContext(options);
        bare.Add(Facility.Create(FacilityCode.SLH, "Tooling's Slaughterhouse", "SLH"));
        bare.SaveChanges();

        Assert.Equal(Guid.Empty, bare.Facilities.Single().MunicipalityId);
    }

    [Fact]
    public void AGlobalReferenceTableIsNotTenantOwned()
    {
        // Municipality itself is not tenant-owned, so it stays readable whatever the scope. The fix must not sweep these
        // up: a tenant that cannot read the municipality table cannot resolve its own identity.
        var options = SharedStore();

        using (var bare = new AppDbContext(options))
        {
            bare.Add(Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, "cantilan-sds", isDefault: true));
            bare.SaveChanges();
        }

        using var a = new AppDbContext(options, new FixedMunicipality(TenantA));
        Assert.Single(a.Municipalities);
    }

    [Fact]
    public void AnAuthenticatedUserWithNoMunicipalityOfItsOwnDependsOnTheDefaultFallback()
    {
        // THE FACT THAT DECIDES THE ORDER OF THE FIX.
        //
        // A user row carries its own MunicipalityId, and Cantilan's original accounts predate that column being filled,
        // so theirs is Guid.Empty. CurrentUserService reads an all-zero claim as NULL, and the accessor then falls back
        // to the default municipality - which is the only reason those accounts see Cantilan's data at all.
        //
        // So removing the fallback for authenticated requests, or failing the filter closed, would lock the office out
        // of its own system BEFORE those rows are stamped. The data has to be corrected first. This test states the
        // dependency so the sequence cannot be forgotten.
        var options = SharedStore();

        using (var writer = new AppDbContext(options, new FixedMunicipality(TenantA)))
        {
            writer.Add(Facility.Create(FacilityCode.NCC, "Cantilan's New Commercial Center", "NCC"));
            writer.SaveChanges();
        }

        // Resolved to the default (what the fallback does today): the office sees its own facility.
        using (var withFallback = new AppDbContext(options, new FixedMunicipality(TenantA)))
            Assert.Single(withFallback.Facilities);

        // Resolved to nothing (what removing the fallback would do to those accounts): today it reads EVERY tenant's
        // rows, and under a fail-closed filter it would read none. Neither is what the office should see.
        using var withoutFallback = new AppDbContext(options, new FixedMunicipality(Guid.Empty));
        Assert.Single(withoutFallback.Facilities);   // today: the no-op filter happens to show it
    }
}
