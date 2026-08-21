using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Testing.Support;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Fees;

/// <summary>
/// An office bills under its own ordinance, or it does not bill.
///
/// <para>
/// Reported by the office: Madrid had never stated a per-kilo weighing fee, and was charging ₱1.00 a kilo. That
/// figure is Cantilan's. Every rate an LGU had not stated resolved to a constant taken from the reference
/// municipality's ordinance, so one LGU's ordinance was quietly collecting money for another's.
/// </para>
///
/// <para>
/// Removing the fallback is only half of it. A charge computed from a rate that is not there would then be
/// raised at zero, which is worse than a wrong figure: it records a day, a head or a trip as collected for
/// nothing, and the office reconciles against it by hand. So the paths that create a charge refuse, and say
/// which rate to set.
/// </para>
/// </summary>
public class UnstatedRatesAreNotBilledTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);

    private sealed class RateResolver(FeeRateSnapshot snapshot) : IFeeRateResolver
    {
        public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot);
    }

    [Fact]
    public void AnOfficeThatHasStatedNothing_ChargesNothing()
    {
        var stated = new FeeRateSnapshot(Array.Empty<FeeRateEntry>());

        Assert.Null(stated.ResolveOrNull(FeeRateKey.NpmFishPerKilo, Today));
        Assert.Equal(0m, stated.Resolve(FeeRateKey.NpmFishPerKilo, Today));
    }

    [Fact]
    public void OneOfficesRate_IsNeverAnothersDefault()
    {
        // The shape of the reported defect: an office states its daily fee but not its weighing fee. The weighing
        // fee must not arrive from anywhere else just because the office is otherwise configured.
        var madrid = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2020, 1, 1)),
        });

        Assert.Equal(40m, madrid.Resolve(FeeRateKey.NpmDailyStall, Today));
        Assert.Null(madrid.ResolveOrNull(FeeRateKey.NpmFishPerKilo, Today));
        Assert.NotEqual(1m, madrid.Resolve(FeeRateKey.NpmFishPerKilo, Today));
    }

    [Fact]
    public void AnUnstatedMonthlyRentStillMeansThirtyDailyFees()
    {
        // Deliberately NOT a refusal: the ordinance may state a month, and where it does not, a month is thirty
        // of the office's own daily fee. That derivation is the office's own arithmetic, not a borrowed figure,
        // and it must survive the fallback being removed.
        var snapshot = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2020, 1, 1)),
        });

        var stall = Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);

        var monthly = stall.ResolveMonthlyRent(
            snapshot.Resolve(FeeRateKey.NpmDailyStall, Today),
            snapshot.Resolve(FeeRateKey.NpmMonthlyStall, Today));

        Assert.Equal(40m * DomainRules.DailyBilledMonthDays, monthly);
    }

    [Fact]
    public void AStatedMonthlyRentIsUsedAsStated()
    {
        var snapshot = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2020, 1, 1)),
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 900m, new DateOnly(2020, 1, 1)),
        });

        var stall = Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);

        var monthly = stall.ResolveMonthlyRent(
            snapshot.Resolve(FeeRateKey.NpmDailyStall, Today),
            snapshot.Resolve(FeeRateKey.NpmMonthlyStall, Today));

        Assert.Equal(900m, monthly);
    }

    [Fact]
    public void TheRefusalNamesTheRateToSet_AndWhereToSetIt()
    {
        var message = FeeRateMessages.NotStated(FeeRateKey.NpmFishPerKilo);

        Assert.Contains("Fish fee", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Facility Configuration", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FeeRateKey.NpmDailyStall)]
    [InlineData(FeeRateKey.SlhHogPerHead)]
    [InlineData(FeeRateKey.SlhLargePerHead)]
    [InlineData(FeeRateKey.TpmVendorDay)]
    [InlineData(FeeRateKey.TrmPerTrip)]
    public void EveryRateAChargeIsBuiltFrom_HasARefusalToOffer(FeeRateKey key)
    {
        // Each of these is the whole amount of a transaction. If one is unstated there is no charge to raise, so
        // the office has to be told rather than handed a zero.
        var message = FeeRateMessages.NotStated(key);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("0.00", message);
    }
}
