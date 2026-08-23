using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Fees;

/// <summary>
/// A rate key belongs to one facility's ordinance, so a row filed against a different facility is not that
/// key's rate. The write path refuses to create one; the resolver must refuse to trust one that already
/// exists, or a mis-filed row would hand one facility's figure to another — the same error as a hardcoded rate.
/// </summary>
public class FeeRateSnapshotFacilityTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 3);

    [Fact]
    public void ARowFiledAgainstAnotherFacility_IsNotUsed()
    {
        var snapshot = new FeeRateSnapshot(new[]
        {
            // The NPM daily fee, mis-filed under the slaughterhouse.
            new FeeRateEntry(FacilityCode.SLH, FeeRateKey.NpmDailyStall, 999m, new DateOnly(2026, 1, 1)),
        });

        // Not that key's rate, so as far as the market is concerned the office has stated nothing.
        Assert.Null(snapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, AsOf));
        Assert.Equal(0m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
    }

    [Fact]
    public void TheFacilitysOwnRow_IsUsed()
    {
        var snapshot = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2026, 1, 1)),
        });

        Assert.Equal(40m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
    }

    [Fact]
    public void AMisfiledRow_DoesNotShadowTheRealOne()
    {
        var snapshot = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2026, 1, 1)),
            // Later date, wrong facility: it must not win on recency.
            new FeeRateEntry(FacilityCode.TPM, FeeRateKey.NpmDailyStall, 999m, new DateOnly(2026, 7, 1)),
        });

        Assert.Equal(40m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
    }

    [Theory]
    [InlineData(FeeRateKey.NpmDailyStall, FacilityCode.NPM)]
    [InlineData(FeeRateKey.NpmMonthlyStall, FacilityCode.NPM)]
    [InlineData(FeeRateKey.NpmFishPerKilo, FacilityCode.NPM)]
    [InlineData(FeeRateKey.ElecPerKwh, FacilityCode.NPM)]
    [InlineData(FeeRateKey.WaterPerCubicMeter, FacilityCode.NPM)]
    [InlineData(FeeRateKey.SlhHogPerHead, FacilityCode.SLH)]
    [InlineData(FeeRateKey.SlhLargePerHead, FacilityCode.SLH)]
    [InlineData(FeeRateKey.TpmVendorDay, FacilityCode.TPM)]
    [InlineData(FeeRateKey.TrmPerTrip, FacilityCode.TRM)]
    public void EveryKey_BelongsToTheFacilityThatConfiguresIt(FeeRateKey key, FacilityCode facility)
    {
        // The two directions must agree, or the resolver would ignore a row the validator accepted.
        Assert.Equal(facility, FacilityRateKeys.OwnerOf(key));
        Assert.Contains(key, FacilityRateKeys.For(facility));
    }

    [Fact]
    public void EveryKeyIsEitherOfferedByItsOwner_OrWithheldOnPurpose()
    {
        // The theory above lists keys by hand, so a key added later could be owned by a facility that never offers it -
        // settable by nobody, and silently unreachable. Every key must therefore be one or the other, and the only
        // withheld ones are the market's per-area daily rates, which are resolved but not yet offered because no billing
        // path reads them (see NpmDailyFeeTests).
        foreach (var key in Enum.GetValues<FeeRateKey>())
        {
            var offered = FacilityRateKeys.For(FacilityRateKeys.OwnerOf(key)).Contains(key);
            var withheldOnPurpose = FacilityRateKeys.IsPerAreaDailyKey(key);

            Assert.True(offered ^ withheldOnPurpose,
                $"{key} is neither offered by {FacilityRateKeys.OwnerOf(key)} nor withheld on purpose.");
        }
    }
}
