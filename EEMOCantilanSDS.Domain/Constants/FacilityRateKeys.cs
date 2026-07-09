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
            { FeeRateKey.NpmDailyStall, FeeRateKey.NpmFishPerKilo, FeeRateKey.ElecPerKwh, FeeRateKey.WaterPerCubicMeter };
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
    }
}
