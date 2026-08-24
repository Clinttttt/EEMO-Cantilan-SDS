using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Caching;

namespace EEMOCantilanSDS.Testing.Infrastructure.Caching;

/// <summary>
/// A restore drops every cached view of the office it restored, and nobody else's.
///
/// Reported from use on 2026-08-24: a vendor added to the barbecue stand and then removed by restoring a backup still
/// counted on that facility's page until the page was reloaded. The restore was correct — the row was gone — but it
/// invalidated NOTHING, so every cached view went on describing the data that had just been rolled back until each
/// entry expired on its own.
///
/// A restore can rewrite any row of any year, so no period, facility or region can be named in advance. The office's
/// whole cache goes, which is what this pins — together with the part that matters just as much in a shared database:
/// that purging one office leaves every other office's cache standing.
/// </summary>
public class TenantCacheInvalidationTests
{
    [Fact]
    public async Task ARestoreDropsEveryRegionOfThatOffice()
    {
        var invalidator = new MemoryEemoCacheInvalidator();

        // The shapes a cached view is tagged with: a period, a facility period, reference data, the activity feed and
        // the cumulative outstanding list.
        var regions = new[]
        {
            EemoCacheRegions.Period("madrid", 2026, 8),
            EemoCacheRegions.Dashboard("madrid", 2026, 8),
            EemoCacheRegions.Reports("madrid", 2025, 12),
            EemoCacheRegions.FacilityPeriod("madrid", FacilityCode.BBQ, 2026, 8),
            EemoCacheRegions.ReferenceData("madrid"),
            EemoCacheRegions.ActivityFeed("madrid"),
            EemoCacheRegions.OutstandingAccounts("madrid"),
        };

        var tokens = regions.Select(r => invalidator.GetChangeToken(r)).ToList();
        Assert.All(tokens, t => Assert.False(t.HasChanged));

        await invalidator.InvalidateTenantAsync("madrid");

        // Every one of them fired, including the 2025 report region no period-based invalidation would have named.
        Assert.All(tokens, t => Assert.True(t.HasChanged));
    }

    [Fact]
    public async Task AnotherOfficesCacheIsLeftStanding()
    {
        // The database is shared. A restore is one office rolling ITS data back, and it must not evict the reference
        // municipality's cached reports in the process.
        var invalidator = new MemoryEemoCacheInvalidator();

        var madrid = invalidator.GetChangeToken(EemoCacheRegions.ReferenceData("madrid"));
        var cantilan = invalidator.GetChangeToken(EemoCacheRegions.ReferenceData("cantilan-sds"));
        var cantilanPeriod = invalidator.GetChangeToken(EemoCacheRegions.Period("cantilan-sds", 2026, 8));

        await invalidator.InvalidateTenantAsync("madrid");

        Assert.True(madrid.HasChanged);
        Assert.False(cantilan.HasChanged);
        Assert.False(cantilanPeriod.HasChanged);
    }

    [Fact]
    public async Task APrefixCannotReachAnOfficeWhoseCodeMerelyStartsTheSameWay()
    {
        // "madrid" must not purge "madrid-north": the prefix carries the colon that every region key uses after the
        // tenant, so one code cannot swallow another that begins with it.
        var invalidator = new MemoryEemoCacheInvalidator();

        var madrid = invalidator.GetChangeToken(EemoCacheRegions.ReferenceData("madrid"));
        var madridNorth = invalidator.GetChangeToken(EemoCacheRegions.ReferenceData("madrid-north"));

        await invalidator.InvalidateTenantAsync("madrid");

        Assert.True(madrid.HasChanged);
        Assert.False(madridNorth.HasChanged);
    }

    [Fact]
    public async Task ABlankTenantPurgesNothing()
    {
        // A blank code normalises to "default" everywhere else in this cache, so purging on one would quietly clear the
        // reference municipality's views on a caller's mistake. It is refused instead.
        var invalidator = new MemoryEemoCacheInvalidator();

        var defaulted = invalidator.GetChangeToken(EemoCacheRegions.ReferenceData(""));
        var named = invalidator.GetChangeToken(EemoCacheRegions.ReferenceData("cantilan-sds"));

        await invalidator.InvalidateTenantAsync("   ");

        Assert.False(defaulted.HasChanged);
        Assert.False(named.HasChanged);
    }
}
