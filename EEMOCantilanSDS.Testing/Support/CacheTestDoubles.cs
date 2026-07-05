using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Testing.Support;

internal static class CacheTestDoubles
{
    public static IEemoCacheInvalidator Invalidator { get; } = new NullEemoCacheInvalidator();
    public static ITenantContext Tenant { get; } = new TestTenantContext();
    public static IEemoAppCache PassthroughCache { get; } = new PassthroughEemoAppCache();

    /// <summary>Resolver with no data rows — every rate falls back to the FeeRates ordinance constant.</summary>
    public static IFeeRateResolver FeeRateResolver { get; } = new StubFeeRateResolver();
}

internal sealed class StubFeeRateResolver : IFeeRateResolver
{
    public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new FeeRateSnapshot(Array.Empty<FeeRateEntry>()));
}

internal sealed class TestTenantContext : ITenantContext
{
    public string TenantCode => TenantConstants.DefaultTenantCode;
}

internal sealed class NullEemoCacheInvalidator : IEemoCacheInvalidator
{
    public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateFacilityPeriodAsync(string tenantCode, EEMOCantilanSDS.Domain.Enums.FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, EEMOCantilanSDS.Domain.Enums.FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class PassthroughEemoAppCache : IEemoAppCache
{
    public Task<T> GetOrCreateAsync<T>(
        string key,
        IReadOnlyCollection<string> regions,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
        => factory(cancellationToken);
}
