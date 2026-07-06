using System;
using System.Linq;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Rates.GetFacilityRates;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Rates
{
    /// <summary>
    /// Read-back of the caller LGU's currently-effective fixed rates: history collapses to the latest row
    /// effective on/before today, future-dated rows are excluded, the metered utility default is included,
    /// and another LGU's rows are never visible.
    /// </summary>
    public class GetFacilityRatesQueryHandlerTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new MunicipalityStampInterceptor())
                .Options;

        [Fact]
        public async Task Returns_CurrentEffectiveRatePerKey_IncludingUtilityDefault()
        {
            var options = Options();
            var lgu = Guid.NewGuid();
            var other = Guid.NewGuid();

            using (var seed = new AppDbContext(options, new FixedMunicipality(lgu)))
            {
                // NPM daily: an old row + a newer one (the newer, still ≤ today, wins).
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25m, new DateOnly(2020, 1, 1), lgu));
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2024, 1, 1), lgu));
                // A future row must NOT be returned as current.
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, PhilippineTime.Today.AddYears(1), lgu));
                // Per-LGU metered utility default rate.
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.ElecPerKwh, 12m, new DateOnly(2020, 1, 1), lgu));
                seed.SaveChanges();
            }
            using (var seedOther = new AppDbContext(options, new FixedMunicipality(other)))
            {
                seedOther.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 99m, new DateOnly(2020, 1, 1), other));
                seedOther.SaveChanges();
            }

            using (var ctx = new AppDbContext(options, new FixedMunicipality(lgu)))
            {
                var result = await new GetFacilityRatesQueryHandler(ctx).Handle(new GetFacilityRatesQuery(), default);
                Assert.True(result.IsSuccess);
                var rates = result.Value!;

                // History collapses to the current amount per key.
                var npm = rates.Single(r => r.Key == FeeRateKey.NpmDailyStall);
                Assert.Equal(30m, npm.Amount);
                Assert.Equal(new DateOnly(2024, 1, 1), npm.EffectiveDate);

                // The seeded metered utility default is returned.
                var elec = rates.Single(r => r.Key == FeeRateKey.ElecPerKwh);
                Assert.Equal(12m, elec.Amount);

                Assert.DoesNotContain(rates, r => r.Amount == 40m); // future-dated excluded
                Assert.DoesNotContain(rates, r => r.Amount == 99m); // other LGU never visible
            }
        }
    }
}
