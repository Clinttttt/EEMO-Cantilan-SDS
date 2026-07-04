using System;
using System.Linq;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Phase3;

/// <summary>
/// Phase 3 — proves the EF global query filter isolates municipality-owned data: a context scoped to
/// one municipality never sees another's rows, IgnoreQueryFilters is the documented escape hatch, and
/// an unresolved (empty) tenant is a no-op (why Cantilan's single-tenant behaviour is unchanged).
/// </summary>
public class MunicipalityIsolationTests
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static void SeedFacility(AppDbContext ctx, Guid municipalityId, string name)
    {
        var facility = Facility.Create(FacilityCode.NPM, name, "NPM");
        ctx.Facilities.Add(facility);
        // MunicipalityId has a private setter (stamped by the interceptor in the app); set it directly
        // via the change tracker for the test.
        ctx.Entry(facility).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = municipalityId;
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Query_ReturnsOnlyCurrentMunicipalityRows()
    {
        var options = Options();
        var munA = Guid.NewGuid();
        var munB = Guid.NewGuid();

        // Seed one facility for each municipality (writes bypass the read filter).
        using (var seed = new AppDbContext(options, new FixedMunicipality(munA)))
        {
            SeedFacility(seed, munA, "Cantilan NPM");
            SeedFacility(seed, munB, "Carmen NPM");
        }

        using (var ctxA = new AppDbContext(options, new FixedMunicipality(munA)))
        {
            var rows = await ctxA.Facilities.ToListAsync();
            Assert.Single(rows);
            Assert.Equal(munA, rows[0].MunicipalityId);
        }

        using (var ctxB = new AppDbContext(options, new FixedMunicipality(munB)))
        {
            var rows = await ctxB.Facilities.ToListAsync();
            Assert.Single(rows);
            Assert.Equal(munB, rows[0].MunicipalityId);
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_SeesAllMunicipalities()
    {
        var options = Options();
        var munA = Guid.NewGuid();
        var munB = Guid.NewGuid();

        using (var seed = new AppDbContext(options, new FixedMunicipality(munA)))
        {
            SeedFacility(seed, munA, "Cantilan NPM");
            SeedFacility(seed, munB, "Carmen NPM");
        }

        using var ctx = new AppDbContext(options, new FixedMunicipality(munA));
        var all = await ctx.Facilities.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task UnresolvedTenant_IsNoOp_ReturnsAllRows()
    {
        // No accessor (empty municipality) — the single-tenant / test path. Filter must not hide anything,
        // which is exactly why adding it left Cantilan's goldens byte-for-byte unchanged.
        var options = Options();
        var munA = Guid.NewGuid();
        var munB = Guid.NewGuid();

        using (var seed = new AppDbContext(options))
        {
            SeedFacility(seed, munA, "Cantilan NPM");
            SeedFacility(seed, munB, "Carmen NPM");
        }

        using var ctx = new AppDbContext(options);
        var all = await ctx.Facilities.ToListAsync();
        Assert.Equal(2, all.Count);
    }
}
