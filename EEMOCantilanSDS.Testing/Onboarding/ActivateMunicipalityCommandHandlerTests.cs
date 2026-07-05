using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Onboarding
{
    /// <summary>
    /// Phase 6 — the activation commit. Proves a staged config becomes a live, isolated LGU: the
    /// municipality flips to Active with its branding, its facilities/rates/Head are created under its own
    /// MunicipalityId (never the operator's), and the default LGU / double-activation are rejected.
    /// </summary>
    public class ActivateMunicipalityCommandHandlerTests
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

        // Seeds the default (Cantilan) + an Upcoming Carmen; returns their ids.
        private static async Task<(Guid cantilanId, Guid carmenId)> SeedRegistryAsync(DbContextOptions<AppDbContext> options)
        {
            using var seed = new AppDbContext(options);
            var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            var carmen = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming, tenantCode: "carmen");
            seed.Municipalities.Add(cantilan);
            seed.Municipalities.Add(carmen);
            await seed.SaveChangesAsync();
            return (cantilan.Id, carmen.Id);
        }

        private static ActivateMunicipalityCommand CarmenConfig(string code = "CARMEN") => new(
            code,
            new ActivationBranding("Carmen Economic Enterprise Office", "Carmen, Surigao del Sur", null),
            new ActivationAdministrator("Maria Santos", "carmen.head", "head@carmen.gov.ph"),
            new List<ActivationFacility>
            {
                new(FacilityCode.NPM, "Carmen Public Market", "CPM", BillingArchetype.DailyStall),
                new(FacilityCode.SLH, "Carmen Slaughterhouse", "CSLH", BillingArchetype.PerHead),
            },
            new List<ActivationRate>
            {
                new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25m),
                new(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 200m),
            });

        [Fact]
        public async Task Activate_GoesLive_And_CreatesScopedData()
        {
            var options = Options();
            var (cantilanId, carmenId) = await SeedRegistryAsync(options);

            // Handler runs as the platform operator (Cantilan-scoped context).
            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx).Handle(CarmenConfig(), default);

                Assert.True(result.IsSuccess);
                Assert.Equal(carmenId, result.Value!.MunicipalityId);
                Assert.Equal("carmen.head", result.Value.AdminUsername);
                Assert.False(string.IsNullOrWhiteSpace(result.Value.TemporaryPassword));
                Assert.Equal(2, result.Value.FacilitiesCreated);
                Assert.Equal(2, result.Value.RatesCreated);
            }

            // The registry record flipped to Active with its branding.
            using (var verify = new AppDbContext(options))
            {
                var carmen = await verify.Municipalities.IgnoreQueryFilters().FirstAsync(m => m.Id == carmenId);
                Assert.Equal(MunicipalityStatus.Active, carmen.Status);
                Assert.True(carmen.IsActive);
                Assert.Equal("Carmen Economic Enterprise Office", carmen.OfficeName);
            }

            // Facilities / rates / Head are all scoped to Carmen (never the operator's Cantilan id).
            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var facilities = await carmenCtx.Facilities.ToListAsync();
                Assert.Equal(2, facilities.Count);
                Assert.All(facilities, f => Assert.Equal(carmenId, f.MunicipalityId));

                var rates = await carmenCtx.FacilityRates.ToListAsync();
                Assert.Equal(2, rates.Count);
                Assert.All(rates, r => Assert.Equal(carmenId, r.MunicipalityId));
                Assert.Equal(25m, rates.First(r => r.RateKey == FeeRateKey.NpmDailyStall).Amount);

                var head = await carmenCtx.AdminUsers.SingleAsync();
                Assert.Equal(carmenId, head.MunicipalityId);
                Assert.Equal(AdminRole.SuperAdmin, head.Role);
                Assert.True(head.MustChangePassword);
            }

            // Isolation: the operator's own (Cantilan) scope sees none of Carmen's rows.
            using (var cantilanCtx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                Assert.Empty(await cantilanCtx.Facilities.ToListAsync());
                Assert.Empty(await cantilanCtx.FacilityRates.ToListAsync());
                Assert.Empty(await cantilanCtx.AdminUsers.ToListAsync());
            }
        }

        [Fact]
        public async Task Activate_RejectsDefaultMunicipality()
        {
            var options = Options();
            var (cantilanId, _) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var result = await new ActivateMunicipalityCommandHandler(ctx).Handle(CarmenConfig(code: "CANTILAN"), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task Activate_RejectsAlreadyActive()
        {
            var options = Options();
            var (cantilanId, _) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var first = await new ActivateMunicipalityCommandHandler(ctx).Handle(CarmenConfig(), default);
            Assert.True(first.IsSuccess);

            using var ctx2 = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var second = await new ActivateMunicipalityCommandHandler(ctx2).Handle(CarmenConfig(), default);
            Assert.False(second.IsSuccess);
        }

        [Fact]
        public async Task Activate_UnknownMunicipality_NotFound()
        {
            var options = Options();
            var (cantilanId, _) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var result = await new ActivateMunicipalityCommandHandler(ctx).Handle(CarmenConfig(code: "NOWHERE"), default);

            Assert.False(result.IsSuccess);
        }
    }
}
