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

        public static async Task SeedAsync(IAppDbContext context)
        {
            if (await context.FacilityRates.IgnoreQueryFilters().AnyAsync()) return;

            var municipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => m.Id)
                .FirstOrDefaultAsync();
            if (municipalityId == Guid.Empty) return; // no default municipality yet — nothing to attribute

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
            await context.SaveChangesAsync();
        }
    }
}
