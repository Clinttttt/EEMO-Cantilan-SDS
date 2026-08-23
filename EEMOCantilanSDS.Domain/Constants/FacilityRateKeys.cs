using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Constants
{
    /// <summary>
    /// The fixed ordinance rate keys that apply to each facility type — the fees a Head can configure for
    /// that facility. Monthly-rental facilities (TCC/NCC/BBQ/ICE) have none: their rent is negotiated per
    /// stall (<c>Stall.MonthlyRate</c>), not a fixed ordinance rate. Mirrors what onboarding seeds and what
    /// the fee resolver reads, so it never drifts from the billing machinery.
    /// </summary>
    public static class FacilityRateKeys
    {
        private static readonly FeeRateKey[] Npm =
            { FeeRateKey.NpmDailyStall, FeeRateKey.NpmMonthlyStall, FeeRateKey.NpmFishPerKilo, FeeRateKey.ElecPerKwh, FeeRateKey.WaterPerCubicMeter };

        /// <summary>
        /// A market's per-area daily rates. Owned by NPM for resolution, but deliberately NOT part of <see cref="Npm"/>
        /// yet: that list is what the office's rate editor offers and what activation accepts, and until the billing
        /// paths read a per-area rate, offering one would let an office set ₱35 for its fish area and be charged ₱30 —
        /// a figure on screen that no collection honours. They join that list in the same change that makes every
        /// billing path read them.
        /// </summary>
        private static readonly FeeRateKey[] NpmPerArea =
            { FeeRateKey.NpmDailyStallVegetable, FeeRateKey.NpmDailyStallFish, FeeRateKey.NpmDailyStallMeat };
        private static readonly FeeRateKey[] Slh = { FeeRateKey.SlhHogPerHead, FeeRateKey.SlhLargePerHead };
        private static readonly FeeRateKey[] Tpm = { FeeRateKey.TpmVendorDay };
        private static readonly FeeRateKey[] Trm = { FeeRateKey.TrmPerTrip };
        private static readonly FeeRateKey[] None = System.Array.Empty<FeeRateKey>();

        public static IReadOnlyList<FeeRateKey> For(FacilityCode code) => code switch
        {
            FacilityCode.NPM => Npm,
            FacilityCode.SLH => Slh,
            FacilityCode.TPM => Tpm,
            FacilityCode.TRM => Trm,
            _ => None, // TCC / NCC / BBQ / ICE — monthly rental, rates live per stall
        };

        /// <summary>
        /// The facility whose ordinance a key belongs to. Stated once here so the resolver and the validator
        /// cannot disagree: a row filed against the wrong facility is not that facility's rate, and must not be
        /// handed out as one.
        /// </summary>
        public static FacilityCode OwnerOf(FeeRateKey key) => key switch
        {
            FeeRateKey.NpmDailyStall or FeeRateKey.NpmMonthlyStall or FeeRateKey.NpmFishPerKilo
                or FeeRateKey.ElecPerKwh or FeeRateKey.WaterPerCubicMeter => FacilityCode.NPM,
            FeeRateKey.NpmDailyStallVegetable or FeeRateKey.NpmDailyStallFish
                or FeeRateKey.NpmDailyStallMeat => FacilityCode.NPM,
            FeeRateKey.SlhHogPerHead or FeeRateKey.SlhLargePerHead => FacilityCode.SLH,
            FeeRateKey.TpmVendorDay => FacilityCode.TPM,
            FeeRateKey.TrmPerTrip => FacilityCode.TRM,
            _ => FacilityCode.NPM,
        };

        /// <summary>
        /// The rate key for ONE collection area of a market, or null for an area the platform keys nothing on (a
        /// market's own area, whose stalls carry their own daily rate).
        /// </summary>
        public static FeeRateKey? PerAreaDailyKey(MarketSection section) => section switch
        {
            MarketSection.VegetableArea => FeeRateKey.NpmDailyStallVegetable,
            MarketSection.FishSection => FeeRateKey.NpmDailyStallFish,
            MarketSection.MeatSection => FeeRateKey.NpmDailyStallMeat,
            _ => null,
        };

        /// <summary>True for the per-area daily rates, which are resolved per area rather than per market.</summary>
        public static bool IsPerAreaDailyKey(FeeRateKey key) => NpmPerArea.Contains(key);
    }
}
