using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Seeders
{
    /// <summary>
    /// Seeds the default municipality's fixed ordinance fee rates into <c>FacilityRates</c> from today's
    /// <see cref="FeeRates"/> constants, so the rate table reproduces the current amounts exactly. Idempotent;
    /// effective from a base date early enough to cover every historical billing period.
    /// </summary>
    public static class FacilityRateSeeder
    {
        private static readonly DateOnly EffectiveFrom = new(2020, 1, 1);
        // Confirmed Cantilan Meat weighing rate, effective from the business-rule checkpoint date. This
        // row is tenant data for the default tenant, not a resolver fallback for other municipalities.
        private static readonly DateOnly MeatWeighingEffectiveFrom = new(2026, 9, 29);

        public static async Task SeedAsync(IAppDbContext context)
        {
            var municipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => m.Id)
                .FirstOrDefaultAsync();
            if (municipalityId == Guid.Empty) return; // no default municipality yet — nothing to attribute

            var hasAnyRates = await context.FacilityRates.IgnoreQueryFilters()
                .AnyAsync(r => r.MunicipalityId == municipalityId);
            if (!hasAnyRates)
            {
                var rates = new[]
                {
                    FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, FeeRates.NpmDailyFee, EffectiveFrom, municipalityId),
                    FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, FeeRates.NpmFishFeePerKilo, EffectiveFrom, municipalityId),
                    FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, FeeRates.SlhHogTotalPerHead, EffectiveFrom, municipalityId),
                    FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, FeeRates.SlhLargeTotalPerHead, EffectiveFrom, municipalityId),
                    FacilityRate.Create(FacilityCode.TPM, FeeRateKey.TpmVendorDay, FeeRates.TpmVendorFee, EffectiveFrom, municipalityId),
                    FacilityRate.Create(FacilityCode.TRM, FeeRateKey.TrmPerTrip, FeeRates.TrmTripFee, EffectiveFrom, municipalityId),
                };
                await context.FacilityRates.AddRangeAsync(rates);
            }

            // The original seeder is intentionally one-time for the historical default-rate set. Add only
            // the newly approved NPM Meat rate when it is absent; preserve any Head-configured history.
            var hasMeatRate = await context.FacilityRates.IgnoreQueryFilters().AnyAsync(r =>
                r.MunicipalityId == municipalityId
                && r.FacilityCode == FacilityCode.NPM
                && r.RateKey == FeeRateKey.NpmMeatPerKilo);
            if (!hasMeatRate)
                await context.FacilityRates.AddAsync(FacilityRate.Create(
                    FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo, FeeRates.NpmMeatFeePerKilo,
                    MeatWeighingEffectiveFrom, municipalityId));

            await context.SaveChangesAsync();
        }
    }
}
