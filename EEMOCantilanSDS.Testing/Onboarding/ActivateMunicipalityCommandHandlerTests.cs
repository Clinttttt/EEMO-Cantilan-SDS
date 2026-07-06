using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Common.Interface.Services;
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

        // Fake caller for the platform-operator authorization check.
        private sealed class FakeCurrentUser(Guid? municipalityId, string? role) : ICurrentUserService
        {
            public bool IsAuthenticated => true;
            public Guid? UserId => Guid.NewGuid();
            public string? Username => "operator";
            public string? Role => role;
            public Guid? CollectorId => null;
            public string? MunicipalityCode => null;
            public Guid? MunicipalityId => municipalityId;
            public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        }

        // The platform operator = a SuperAdmin of the default (Cantilan) municipality.
        private static ICurrentUserService Operator(Guid defaultMunicipalityId) => new FakeCurrentUser(defaultMunicipalityId, "SuperAdmin");

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
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(CarmenConfig(), default);

                Assert.True(result.IsSuccess);
                Assert.Equal(carmenId, result.Value!.MunicipalityId);
                Assert.Equal("carmen.head", result.Value.AdminUsername);
                Assert.False(string.IsNullOrWhiteSpace(result.Value.ActivationToken));
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
                Assert.False(head.IsActive);                                  // inactive until the Head activates
                Assert.False(string.IsNullOrEmpty(head.ActivationTokenHash));  // one-time link token issued
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
        public async Task Activate_ProvisionsStalls_ScopedToLgu()
        {
            var options = Options();
            var (cantilanId, carmenId) = await SeedRegistryAsync(options);

            var config = new ActivateMunicipalityCommand(
                "CARMEN",
                new ActivationBranding("Carmen EEO", null, null),
                new ActivationAdministrator("Maria Santos", "carmen.head", "head@carmen.gov.ph"),
                new List<ActivationFacility>
                {
                    new(FacilityCode.NPM, "Carmen Public Market", "CPM", BillingArchetype.DailyStall, new List<ActivationStallGroup>
                    {
                        new(40, 0m, 25m, ApplicableFees.DailyRental | ApplicableFees.FishFee, MarketSection.FishSection),
                        new(30, 0m, 25m, ApplicableFees.DailyRental, MarketSection.MeatSection),
                    }),
                    new(FacilityCode.TCC, "Carmen Commercial Center", "CCC", BillingArchetype.MonthlyRental, new List<ActivationStallGroup>
                    {
                        new(24, 2400m, null, ApplicableFees.BaseRental),
                    }),
                },
                new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25m) });

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(config, default);
                Assert.True(result.IsSuccess);
                Assert.Equal(94, result.Value!.StallsCreated); // 40 + 30 + 24
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var stalls = await carmenCtx.Stalls.ToListAsync();
                Assert.Equal(94, stalls.Count);
                Assert.All(stalls, s => Assert.Equal(carmenId, s.MunicipalityId));
                Assert.Equal(40, stalls.Count(s => s.Section == MarketSection.FishSection));
                Assert.Equal(24, stalls.Count(s => s.MonthlyRate == 2400m));
            }
        }

        [Fact]
        public async Task Activate_SeedsCustomAnimals_ScopedToLgu()
        {
            var options = Options();
            var (cantilanId, carmenId) = await SeedRegistryAsync(options);

            var config = new ActivateMunicipalityCommand(
                "CARMEN",
                new ActivationBranding("Carmen EEO", null, null),
                new ActivationAdministrator("Maria Santos", "carmen.head", "head@carmen.gov.ph"),
                new List<ActivationFacility>
                {
                    new(FacilityCode.SLH, "Carmen Slaughterhouse", "CSLH", BillingArchetype.PerHead),
                },
                new List<ActivationRate> { new(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 200m) },
                new List<ActivationCustomAnimal>
                {
                    new("Goat", 150m),
                    new("Chicken", 20m),
                });

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(config, default);
                Assert.True(result.IsSuccess);
                Assert.Equal(2, result.Value!.CustomAnimalTypesCreated);
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var animals = await carmenCtx.SlaughterAnimalRates.ToListAsync();
                Assert.Equal(2, animals.Count);
                Assert.All(animals, a => Assert.Equal(carmenId, a.MunicipalityId));
                Assert.All(animals, a => Assert.True(a.IsActive));
                Assert.Equal(150m, animals.First(a => a.AnimalName == "Goat").RatePerHead);
            }

            // Cantilan (the operator's own LGU) has no custom animals of its own.
            using (var cantilanCtx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                Assert.Empty(await cantilanCtx.SlaughterAnimalRates.ToListAsync());
            }
        }

        [Fact]
        public async Task Activate_SeedsOrSeries_ScopedToLgu()
        {
            var options = Options();
            var (cantilanId, carmenId) = await SeedRegistryAsync(options);

            var config = new ActivateMunicipalityCommand(
                "CARMEN",
                new ActivationBranding("Carmen EEO", null, null),
                new ActivationAdministrator("Maria Santos", "carmen.head", "head@carmen.gov.ph"),
                new List<ActivationFacility> { new(FacilityCode.NPM, "Carmen Public Market", "CPM", BillingArchetype.DailyStall) },
                new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25m) },
                CustomAnimals: null,
                OrSeries: new ActivationOrSeries("CARM-2026-", 1, 6, true));

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(config, default);
                Assert.True(result.IsSuccess);
                Assert.True(result.Value!.OrSeriesConfigured);
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var cfg = await carmenCtx.OrSeriesConfigs.SingleAsync();
                Assert.Equal(carmenId, cfg.MunicipalityId);
                Assert.True(cfg.IsEnabled);
                Assert.Equal("CARM-2026-000001", cfg.Peek());
            }

            // Cantilan (the operator's own LGU) has no OR-series of its own.
            using (var cantilanCtx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                Assert.Empty(await cantilanCtx.OrSeriesConfigs.ToListAsync());
            }
        }

        [Fact]
        public async Task Activate_RejectsDefaultMunicipality()        {
            var options = Options();
            var (cantilanId, _) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(CarmenConfig(code: "CANTILAN"), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task Activate_RejectsAlreadyActive()
        {
            var options = Options();
            var (cantilanId, _) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var first = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(CarmenConfig(), default);
            Assert.True(first.IsSuccess);

            using var ctx2 = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var second = await new ActivateMunicipalityCommandHandler(ctx2, Operator(cantilanId)).Handle(CarmenConfig(), default);
            Assert.False(second.IsSuccess);
        }

        [Fact]
        public async Task Activate_UnknownMunicipality_NotFound()
        {
            var options = Options();
            var (cantilanId, _) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(cantilanId)).Handle(CarmenConfig(code: "NOWHERE"), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task Activate_NonPlatformOperator_Forbidden()
        {
            var options = Options();
            var (cantilanId, carmenId) = await SeedRegistryAsync(options);

            // A SuperAdmin of a NON-default LGU (Carmen) is not the platform operator.
            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, new FakeCurrentUser(carmenId, "SuperAdmin"))
                    .Handle(CarmenConfig(), default);
                Assert.False(result.IsSuccess);
            }

            // An Admin (not SuperAdmin) of the default LGU is likewise rejected.
            using (var ctx2 = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result2 = await new ActivateMunicipalityCommandHandler(ctx2, new FakeCurrentUser(cantilanId, "Admin"))
                    .Handle(CarmenConfig(), default);
                Assert.False(result2.IsSuccess);
            }

            // The Upcoming municipality must remain Upcoming after rejected attempts.
            using (var verify = new AppDbContext(options))
            {
                var carmen = await verify.Municipalities.IgnoreQueryFilters().FirstAsync(m => m.Id == carmenId);
                Assert.Equal(MunicipalityStatus.Upcoming, carmen.Status);
            }
        }
    }
}
