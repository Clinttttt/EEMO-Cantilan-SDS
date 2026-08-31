using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Support;

internal static class CacheTestDoubles
{
    public static IEemoCacheInvalidator Invalidator { get; } = new NullEemoCacheInvalidator();
    public static ITenantContext Tenant { get; } = new TestTenantContext();
    public static IEemoAppCache PassthroughCache { get; } = new PassthroughEemoAppCache();

    /// <summary>
    /// An office that HAS stated its ordinance rates. It used to be a resolver with no rows at all, which worked
    /// only because an unstated rate then fell back to the reference municipality's constant — the borrowing that
    /// let one LGU bill another's figures. With that fallback gone, "no rows" means an office that cannot bill,
    /// so a test about billing has to say what the office charges. These are the reference amounts, stated
    /// explicitly, so every existing expectation still reads the same figure.
    /// </summary>
    public static IFeeRateResolver FeeRateResolver { get; } = new StubFeeRateResolver();

    /// <summary>The same office, measuring a market month by the days it has.</summary>
    public static IFeeRateResolver PureDaysFeeRateResolver { get; } = new StubPureDaysFeeRateResolver();

    /// <summary>Market-day provider fixed to Friday (the Cantilan default) for existing tests.</summary>
    public static EEMOCantilanSDS.Application.Common.Interface.Services.ITpmMarketDayProvider TpmMarketDay { get; } = new StubTpmMarketDayProvider();
}

internal sealed class StubTpmMarketDayProvider : EEMOCantilanSDS.Application.Common.Interface.Services.ITpmMarketDayProvider
{
    /// <summary>An office whose market day is Friday and has never moved it.</summary>
    public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default)
        => Task.FromResult(DayOfWeek.Friday);

    public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default)
    {
        var dates = new List<DateOnly>();
        var first = new DateOnly(year, month, 1);
        for (var date = first; date.Month == month; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Friday) dates.Add(date);
        }
        return Task.FromResult<IReadOnlyList<DateOnly>>(dates);
    }
}

internal sealed class StubFeeRateResolver : IFeeRateResolver
{
    public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(TestFeeRates.StatedOrdinance());
}

/// <summary>
/// The same stated ordinance, for an office that measures a market month by the DAYS it has.
/// </summary>
/// <remarks>
/// Its own double rather than a parameter on the other, so a test that says nothing about a basis cannot accidentally be
/// run on one: every existing expectation in this suite belongs to an office on the monthly goal.
/// </remarks>
internal sealed class StubPureDaysFeeRateResolver : IFeeRateResolver
{
    public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(TestFeeRates.StatedOrdinanceOnPureDays());
}

/// <summary>
/// The rate rows a test office states, so a test about billing bills under an ordinance of its own rather than
/// under another municipality's constants. Effective from a date long past, so every asOf in the suite is covered.
/// </summary>
internal static class TestFeeRates
{
    private static readonly DateOnly EffectiveFrom = new(2020, 1, 1);

    public static FeeRateSnapshot StatedOrdinance() => new(Entries());

    /// <summary>The same rates, for an office whose market month owes the days it has.</summary>
    public static FeeRateSnapshot StatedOrdinanceOnPureDays() =>
        new(Entries(), null, EEMOCantilanSDS.Domain.Enums.NpmMonthBasis.PureDays);

    /// <summary>The same amounts the suite has always expected, now stated by the office rather than assumed.</summary>
    public static IEnumerable<FeeRateEntry> Entries()
    {
        yield return new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, FeeRates.NpmDailyFee, EffectiveFrom);
        yield return new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, FeeRates.NpmFishFeePerKilo, EffectiveFrom);
        yield return new FeeRateEntry(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, FeeRates.SlhHogTotalPerHead, EffectiveFrom);
        yield return new FeeRateEntry(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, FeeRates.SlhLargeTotalPerHead, EffectiveFrom);
        yield return new FeeRateEntry(FacilityCode.TPM, FeeRateKey.TpmVendorDay, FeeRates.TpmVendorFee, EffectiveFrom);
        yield return new FeeRateEntry(FacilityCode.TRM, FeeRateKey.TrmPerTrip, FeeRates.TrmTripFee, EffectiveFrom);
    }
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

    public Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default)
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
