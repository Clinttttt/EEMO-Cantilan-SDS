using System.Collections.Concurrent;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.Primitives;

namespace EEMOCantilanSDS.Infrastructure.Caching;

public sealed class MemoryEemoCacheInvalidator : IEemoCacheInvalidator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> regions = new(StringComparer.Ordinal);

    public IChangeToken GetChangeToken(string region)
        => new CancellationChangeToken(GetOrCreateSource(region).Token);

    public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default)
    {
        if (regions.TryRemove(NormalizeRegion(region), out var source))
            source.Cancel();

        return Task.CompletedTask;
    }

    public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default)
        => InvalidateRegionsAsync(
            new[]
            {
                EemoCacheRegions.Period(tenantCode, year, month),
                EemoCacheRegions.Dashboard(tenantCode, year, month),
                EemoCacheRegions.Reports(tenantCode, year, month),
                EemoCacheRegions.ActivityFeed(tenantCode)
            },
            cancellationToken);

    public Task InvalidateFacilityPeriodAsync(
        string tenantCode,
        FacilityCode facilityCode,
        int year,
        int month,
        CancellationToken cancellationToken = default)
        => InvalidateRegionsAsync(
            new[]
            {
                EemoCacheRegions.Period(tenantCode, year, month),
                EemoCacheRegions.Dashboard(tenantCode, year, month),
                EemoCacheRegions.Reports(tenantCode, year, month),
                EemoCacheRegions.FacilityPeriod(tenantCode, facilityCode, year, month),
                EemoCacheRegions.ActivityFeed(tenantCode)
            },
            cancellationToken);

    public Task InvalidatePaymentAffectedViewsAsync(
        string tenantCode,
        FacilityCode? facilityCode,
        int year,
        int month,
        CancellationToken cancellationToken = default)
        => facilityCode is FacilityCode facility
            ? InvalidateFacilityPeriodAsync(tenantCode, facility, year, month, cancellationToken)
            : InvalidatePeriodAsync(tenantCode, year, month, cancellationToken);

    public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default)
        => InvalidateRegionAsync(EemoCacheRegions.ReferenceData(tenantCode), cancellationToken);

    private async Task InvalidateRegionsAsync(IEnumerable<string> regionNames, CancellationToken cancellationToken)
    {
        foreach (var region in regionNames.Distinct(StringComparer.Ordinal))
            await InvalidateRegionAsync(region, cancellationToken);
    }

    private CancellationTokenSource GetOrCreateSource(string region)
        => regions.GetOrAdd(NormalizeRegion(region), _ => new CancellationTokenSource());

    private static string NormalizeRegion(string region)
        => string.IsNullOrWhiteSpace(region) ? "default" : region.Trim().ToLowerInvariant();
}
