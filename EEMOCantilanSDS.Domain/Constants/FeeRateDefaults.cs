using System;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Constants
{
    /// <summary>
    /// Maps each fixed <see cref="FeeRateKey"/> to its hard-coded <see cref="FeeRates"/> constant. This is the
    /// single fallback used when a municipality has no <c>FacilityRate</c> row for that key — it reproduces
    /// today's Cantilan amounts exactly, so a tenant seeded from these constants (Phase 4B-i) resolves to
    /// byte-for-byte identical values. New rates live in the <c>FacilityRate</c> table and override this.
    /// </summary>
    public static class FeeRateDefaults
    {
        /// <summary>The ordinance-constant amount for a fixed rate key (the fallback when no data row exists).</summary>
        public static decimal For(FeeRateKey key) => key switch
        {
            FeeRateKey.NpmDailyStall => FeeRates.NpmDailyFee,
            FeeRateKey.NpmFishPerKilo => FeeRates.NpmFishFeePerKilo,
            FeeRateKey.SlhHogPerHead => FeeRates.SlhHogTotalPerHead,
            FeeRateKey.SlhLargePerHead => FeeRates.SlhLargeTotalPerHead,
            FeeRateKey.TpmVendorDay => FeeRates.TpmVendorFee,
            FeeRateKey.TrmPerTrip => FeeRates.TrmTripFee,
            // Metered add-ons have no ordinance constant — the rate is entered per bill. A 0 default means
            // "unset"; an LGU may seed its own default (ElecPerKwh/WaterPerCubicMeter) at activation.
            FeeRateKey.ElecPerKwh => 0m,
            FeeRateKey.WaterPerCubicMeter => 0m,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No fee-rate default for this key.")
        };
    }
}
