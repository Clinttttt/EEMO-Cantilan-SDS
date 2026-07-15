using System.Collections.Concurrent;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.Primitives;

namespace EEMOCantilanSDS.Infrastructure.Caching;

public sealed class MemoryEemoCacheInvalidator : IEemoCacheInvalidator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> regions = new(StringComparer.Ordinal);

    public IChangeToken GetChangeToken(string region)
    {
        var normalized = NormalizeRegion(region);
        while (true)
        {
            var source = GetOrCreateSource(normalized);
            try
            {
                return new CancellationChangeToken(source.Token);
            }
            catch (ObjectDisposedException)
            {
                // The source was invalidated (cancelled + disposed) between GetOrAdd and reading .Token.
                // Drop the stale entry only if it's still the disposed one (atomic), then retry with a fresh source.
                regions.TryRemove(new KeyValuePair<string, CancellationTokenSource>(normalized, source));
            }
        }
    }

    public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default)
    {
        if (regions.TryRemove(NormalizeRegion(region), out var source))
        {
            // Cancel fires the expiration tokens (evicting the region's cache entries) synchronously; the
            // source is then removed from the map and any future GetChangeToken creates a fresh one, so it
            // is safe to dispose here rather than leaking a CancellationTokenSource per invalidated region.
            source.Cancel();
            source.Dispose();
        }

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
