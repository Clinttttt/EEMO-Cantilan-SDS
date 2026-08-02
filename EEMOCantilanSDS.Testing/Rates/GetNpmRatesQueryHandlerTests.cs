using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Rates
{
    /// <summary>
    /// The Add Vendor UI reads the tenant's NPM daily + fish rates from this query. It must reflect a
    /// custom LGU rate (e.g. ₱40/day) and fall back to the ordinance constants for a tenant with no rows,
    /// so Cantilan keeps showing ₱30/day + ₱1/kg.
    /// </summary>
    public class GetNpmRatesQueryHandlerTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        /// <summary>
        /// A caller with no municipality claim — these tests are about the RATES, and nothing here is exempt from
        /// the monthly-rent question, which is asked of anything that cannot prove it is the reference tenant.
        /// </summary>
        private static ICurrentUserService NoMunicipalityClaim
        {
            get
            {
                var user = new Mock<ICurrentUserService>();
                user.SetupGet(u => u.MunicipalityId).Returns((Guid?)null);
                return user.Object;
            }
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        [Fact]
        public async Task Returns_The_Tenants_Custom_Npm_Rates()
        {
            var options = Options();
            var lgu = Guid.NewGuid();

            using (var seed = new AppDbContext(options, new FixedMunicipality(lgu)))
            {
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2020, 1, 1), lgu));
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 2m, new DateOnly(2020, 1, 1), lgu));
                await seed.SaveChangesAsync();
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(lgu));
            var result = await new GetNpmRatesQueryHandler(new FeeRateResolver(ctx), NoMunicipalityClaim, ctx).Handle(new GetNpmRatesQuery(), default);

            Assert.True(result.IsSuccess);
            Assert.Equal(40m, result.Value!.DailyRate);
            Assert.Equal(2m, result.Value!.FishRate);
        }

        [Fact]
        public async Task FallsBack_To_Ordinance_Constants_For_A_Tenant_With_No_Rows()
        {
            var options = Options();
            var cantilan = Guid.NewGuid();

            // No FacilityRate rows for this tenant → the resolver falls back to the ordinance constants.
            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilan));
            var result = await new GetNpmRatesQueryHandler(new FeeRateResolver(ctx), NoMunicipalityClaim, ctx).Handle(new GetNpmRatesQuery(), default);

            Assert.True(result.IsSuccess);
            Assert.Equal(FeeRates.NpmDailyFee, result.Value!.DailyRate);   // ₱30
            Assert.Equal(FeeRates.NpmFishFeePerKilo, result.Value!.FishRate); // ₱1
        }
    }
}
