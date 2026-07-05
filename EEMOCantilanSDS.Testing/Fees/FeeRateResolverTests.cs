using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Fees
{
    /// <summary>
    /// Phase 4B — proves the fee-rate resolver reads the CURRENT municipality's FacilityRate rows (so a
    /// second LGU gets its own NPM rates), isolates them from other tenants, and falls back to the ordinance
    /// constants when a tenant has no rows (why Cantilan is byte-for-byte unchanged).
    /// </summary>
    public class FeeRateResolverTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        private static readonly DateOnly AsOf = new(2026, 6, 15);
        private static readonly DateOnly Effective = new(2020, 1, 1);

        [Fact]
        public async Task Resolves_The_Current_Municipalitys_Rates()
        {
            var options = Options();
            var carmen = Guid.NewGuid();

            // Carmen sets its own NPM rates: ₱25/day and ₱2/kg fish.
            using (var seed = new AppDbContext(options, new FixedMunicipality(carmen)))
            {
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25m, Effective, carmen));
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 2m, Effective, carmen));
                await seed.SaveChangesAsync();
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(carmen));
            var snapshot = await new FeeRateResolver(ctx).GetSnapshotAsync();

            Assert.Equal(25m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
            Assert.Equal(2m, snapshot.Resolve(FeeRateKey.NpmFishPerKilo, AsOf));
        }

        [Fact]
        public async Task Does_Not_See_Another_Municipalitys_Rates_And_Falls_Back_To_Constants()
        {
            var options = Options();
            var carmen = Guid.NewGuid();
            var cantilan = Guid.NewGuid();

            // Only Carmen has custom rates seeded.
            using (var seed = new AppDbContext(options, new FixedMunicipality(carmen)))
            {
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25m, Effective, carmen));
                await seed.SaveChangesAsync();
            }

            // A context scoped to Cantilan (no rows of its own) must not see Carmen's rate — it falls back
            // to the ordinance constant.
            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilan));
            var snapshot = await new FeeRateResolver(ctx).GetSnapshotAsync();

            Assert.Equal(FeeRates.NpmDailyFee, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
            Assert.Equal(30m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
        }
    }
}
