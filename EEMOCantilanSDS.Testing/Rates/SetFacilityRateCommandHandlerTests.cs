using System;
using System.Linq;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Testing.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Rates
{
    /// <summary>
    /// Self-service fee editing: an LGU Head changes one of their own fixed rates. The edit is scoped to the
    /// caller's municipality, effective today forward (past periods keep the old amount), and never touches
    /// another LGU. Same-day edits update in place instead of stacking rows.
    /// </summary>
    public class SetFacilityRateCommandHandlerTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        // Interceptor wired so new rows get tenant-stamped exactly as in production.
        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new MunicipalityStampInterceptor())
                .Options;

        private static readonly DateOnly SeedEffective = new(2020, 1, 1);

        private static void SeedRate(DbContextOptions<AppDbContext> options, Guid municipalityId, decimal amount)
        {
            using var seed = new AppDbContext(options, new FixedMunicipality(municipalityId));
            seed.FacilityRates.Add(FacilityRate.Create(
                FacilityCode.NPM, FeeRateKey.NpmDailyStall, amount, SeedEffective, municipalityId));
            seed.SaveChanges();
        }

        [Fact]
        public async Task SetRate_TakesEffectToday_PreservesPast()
        {
            var options = Options();
            var lgu = Guid.NewGuid();
            SeedRate(options, lgu, 30m); // existing ordinance rate since 2020

            using (var ctx = new AppDbContext(options, new FixedMunicipality(lgu)))
            {
                var result = await new SetFacilityRateCommandHandler(ctx, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
                    .Handle(new SetFacilityRateCommand(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m), default);
                Assert.True(result.IsSuccess);
            }

            using (var verify = new AppDbContext(options, new FixedMunicipality(lgu)))
            {
                var snapshot = await new FeeRateResolver(verify).GetSnapshotAsync();
                // Today onward -> new amount; a past date -> the prior amount (history preserved).
                Assert.Equal(35m, snapshot.Resolve(FeeRateKey.NpmDailyStall, PhilippineTime.Today));
                Assert.Equal(30m, snapshot.Resolve(FeeRateKey.NpmDailyStall, new DateOnly(2021, 6, 1)));

                // The new row is scoped to the caller's LGU.
                var todays = await verify.FacilityRates
                    .Where(r => r.RateKey == FeeRateKey.NpmDailyStall && r.EffectiveDate == PhilippineTime.Today)
                    .ToListAsync();
                Assert.Single(todays);
                Assert.Equal(lgu, todays[0].MunicipalityId);
            }
        }

        [Fact]
        public async Task SetRate_SameDay_UpdatesInPlace()
        {
            var options = Options();
            var lgu = Guid.NewGuid();
            SeedRate(options, lgu, 30m);

            using (var ctx = new AppDbContext(options, new FixedMunicipality(lgu)))
                await new SetFacilityRateCommandHandler(ctx, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
                    .Handle(new SetFacilityRateCommand(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m), default);

            using (var ctx = new AppDbContext(options, new FixedMunicipality(lgu)))
                await new SetFacilityRateCommandHandler(ctx, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
                    .Handle(new SetFacilityRateCommand(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m), default);

            using (var verify = new AppDbContext(options, new FixedMunicipality(lgu)))
            {
                var todays = await verify.FacilityRates
                    .Where(r => r.RateKey == FeeRateKey.NpmDailyStall && r.EffectiveDate == PhilippineTime.Today)
                    .ToListAsync();
                Assert.Single(todays);            // no duplicate rows for the same day
                Assert.Equal(40m, todays[0].Amount);
                var snapshot = await new FeeRateResolver(verify).GetSnapshotAsync();
                Assert.Equal(40m, snapshot.Resolve(FeeRateKey.NpmDailyStall, PhilippineTime.Today));
            }
        }

        [Fact]
        public async Task SetRate_DoesNotAffectAnotherLgu()
        {
            var options = Options();
            var lguA = Guid.NewGuid();
            var lguB = Guid.NewGuid();
            SeedRate(options, lguA, 30m);
            SeedRate(options, lguB, 20m); // B's own rate

            using (var ctx = new AppDbContext(options, new FixedMunicipality(lguA)))
                await new SetFacilityRateCommandHandler(ctx, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
                    .Handle(new SetFacilityRateCommand(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m), default);

            using (var verifyB = new AppDbContext(options, new FixedMunicipality(lguB)))
            {
                var snapshot = await new FeeRateResolver(verifyB).GetSnapshotAsync();
                Assert.Equal(20m, snapshot.Resolve(FeeRateKey.NpmDailyStall, PhilippineTime.Today));
                Assert.DoesNotContain(await verifyB.FacilityRates.ToListAsync(), r => r.Amount == 35m);
            }
        }
    }
}
