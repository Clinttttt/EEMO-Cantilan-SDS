using EEMOCantilanSDS.Infrastructure.Security;
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

        // Fake caller for the platform-operator authorization check. Carries a user id, because the guard reads the
        // IsPlatformOperator flag from the account's own row: being a SuperAdmin of the default municipality no
        // longer makes anybody the operator, so a test acting as the operator has to BE one.
        private sealed class FakeCurrentUser(Guid? userId, Guid? municipalityId, string? role) : ICurrentUserService
        {
            public bool IsAuthenticated => true;
            public Guid? UserId => userId;
            public string? Username => "operator";
            public string? Role => role;
            public Guid? CollectorId => null;
            public string? MunicipalityCode => null;
            public Guid? MunicipalityId => municipalityId;
            public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        }

        /// <summary>The dedicated console operator: the only account that may activate an LGU.</summary>
        private static ICurrentUserService Operator(Guid operatorUserId, Guid municipalityId) =>
            new FakeCurrentUser(operatorUserId, municipalityId, "SuperAdmin");

        /// <summary>A municipality's own Head, who may not.</summary>
        private static ICurrentUserService Head(Guid municipalityId) =>
            new FakeCurrentUser(Guid.NewGuid(), municipalityId, "SuperAdmin");

        // Best-effort email is a side-effect of activation; tests don't assert on it.
        private sealed class NoOpEmailSender : IEmailSender
        {
            public Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, System.Threading.CancellationToken ct = default)
                => Task.FromResult(false);
        }
        private static readonly IEmailSender Email = new NoOpEmailSender();

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        // Seeds the default (Cantilan) + an Upcoming Carmen; returns their ids.
        private static async Task<(Guid cantilanId, Guid carmenId, Guid operatorId)> SeedRegistryAsync(DbContextOptions<AppDbContext> options)
        {
            using var seed = new AppDbContext(options);
            var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            var carmen = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming, tenantCode: "carmen");
            seed.Municipalities.Add(cantilan);
            seed.Municipalities.Add(carmen);
            await seed.SaveChangesAsync();
// The dedicated console operator. The guard reads the IsPlatformOperator flag off the caller's own
            // account row: no municipality's Head is the operator any more, including the default municipality's,
            // so a test that activates has to act as this account.
            var op = AdminUser.Create(
                "Console Operator", "console.op", "op@stalltrack.site", TestPasswords.Hash("OpPass123!"),
                AdminRole.SuperAdmin, cantilan.Id, isActive: true, isPlatformOperator: true);
            seed.AdminUsers.Add(op);
            await seed.SaveChangesAsync();

            return (cantilan.Id, carmen.Id, op.Id);
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

        // Carmen's config with the LGU's own names for its market's collection areas carried on the command.
        private static ActivateMunicipalityCommand CarmenConfigWithSectionLabels(ActivationSectionLabels labels)
        {
            var baseConfig = CarmenConfig();
            return baseConfig with
            {
                Facilities = new List<ActivationFacility>
                {
                    new(FacilityCode.NPM, "Carmen Public Market", "CPM", BillingArchetype.DailyStall, null, labels),
                    new(FacilityCode.SLH, "Carmen Slaughterhouse", "CSLH", BillingArchetype.PerHead),
                }
            };
        }

        [Fact]
        public async Task Activate_NamesSectionAreas_AsTheLguNamedThem()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var command = CarmenConfigWithSectionLabels(new ActivationSectionLabels("Gulayan", "Isda", "Karne"));
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(command, default);
                Assert.True(result.IsSuccess);
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                // The office's own words, in its own language, stored verbatim against the area it named.
                var npm = await carmenCtx.Facilities.FirstAsync(f => f.Code == FacilityCode.NPM);
                Assert.Equal("Gulayan", npm.VegetableSectionLabel);
                Assert.Equal("Isda", npm.FishSectionLabel);
                Assert.Equal("Karne", npm.MeatSectionLabel);
            }
        }

        [Fact]
        public async Task Activate_NamesSectionAreas_FromTheDraftDeclaration_WhenCommandCarriesNone()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            // The LGU declared which collection area each of its sections is; the names are its own and are
            // not English. Guessing from the wording is what dropped "Isda" and "Karne" for Madrid.
            using (var seed = new AppDbContext(options))
            {
                var draft = EEMOCantilanSDS.Domain.Entities.Onboarding.OnboardingDraft.Create(
                    System.Guid.NewGuid(), "Carmen", "Surigao del Sur", "tok-carmen", System.DateTime.UtcNow.AddDays(7));
                draft.UpdateConfig(
                    "{\"facilities\":[{\"catalogKey\":\"public_market\",\"archetype\":\"DailyStall\",\"sections\":["
                    + "{\"name\":\"Gulayan\",\"kind\":\"VegetableArea\"},"
                    + "{\"name\":\"Isda\",\"kind\":\"FishSection\"},"
                    + "{\"name\":\"Karne\",\"kind\":\"MeatSection\"}]}]}",
                    "LGU");
                seed.OnboardingDrafts.Add(draft);
                await seed.SaveChangesAsync();
            }

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(), default);
                Assert.True(result.IsSuccess);
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var npm = await carmenCtx.Facilities.FirstAsync(f => f.Code == FacilityCode.NPM);
                Assert.Equal("Gulayan", npm.VegetableSectionLabel);
                Assert.Equal("Isda", npm.FishSectionLabel);
                Assert.Equal("Karne", npm.MeatSectionLabel);
            }
        }

        [Fact]
        public async Task Activate_NeverReadsASectionsMeaning_FromItsWording()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            // A draft saved before the LGU was asked which area each section is. The wording says "Fish" and
            // "Meat" in plain English, and it is still not evidence: an undeclared area keeps the platform's
            // canonical wording, which the Head corrects in the facility Configuration drawer.
            using (var seed = new AppDbContext(options))
            {
                var draft = EEMOCantilanSDS.Domain.Entities.Onboarding.OnboardingDraft.Create(
                    System.Guid.NewGuid(), "Carmen", "Surigao del Sur", "tok-carmen", System.DateTime.UtcNow.AddDays(7));
                draft.UpdateConfig(
                    "{\"facilities\":[{\"catalogKey\":\"public_market\",\"archetype\":\"DailyStall\",\"sections\":["
                    + "{\"name\":\"Vegetables\"},{\"name\":\"Fish Vendors\"},{\"name\":\"Meat Section\"}]}]}",
                    "LGU");
                seed.OnboardingDrafts.Add(draft);
                await seed.SaveChangesAsync();
            }

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(), default);
                Assert.True(result.IsSuccess);
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var npm = await carmenCtx.Facilities.FirstAsync(f => f.Code == FacilityCode.NPM);
                Assert.Null(npm.VegetableSectionLabel);
                Assert.Null(npm.FishSectionLabel);
                Assert.Null(npm.MeatSectionLabel);
            }
        }

        [Fact]
        public async Task Activate_LeavesAnUnnamedAreaCanonical()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                // The LGU named only its fish area; the other two keep the canonical wording.
                var command = CarmenConfigWithSectionLabels(new ActivationSectionLabels(null, "Isda", null));
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(command, default);
                Assert.True(result.IsSuccess);
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                var npm = await carmenCtx.Facilities.FirstAsync(f => f.Code == FacilityCode.NPM);
                Assert.Null(npm.VegetableSectionLabel);
                Assert.Equal("Isda", npm.FishSectionLabel);
                Assert.Null(npm.MeatSectionLabel);
            }
        }

        [Fact]
        public async Task Activate_GoesLive_And_CreatesScopedData()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            // Handler runs as the platform operator (Cantilan-scoped context).
            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(), default);

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

                // Carmen's Head must not appear in the operator's own scope. Asserted by name rather than by an
                // empty set, because the console operator account itself lives here: the platform creates it under
                // the default municipality's id, so "no users at all" would be asserting the wrong thing.
                var visible = await cantilanCtx.AdminUsers.Select(u => u.Email).ToListAsync();
                Assert.DoesNotContain("head@carmen.gov.ph", visible);
                Assert.Equal(new[] { "op@stalltrack.site" }, visible);
            }
        }

        [Fact]
        public async Task Activate_IgnoresStallGroups_CreatesNoStalls()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            // Even if a (legacy) client sends StallGroups, activation must NOT provision stalls — stalls
            // and their occupants/payors are created in the live portal, never at onboarding.
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
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(config, default);
                Assert.True(result.IsSuccess);
                Assert.Equal(0, result.Value!.StallsCreated);   // StallGroups ignored — no stalls provisioned
                Assert.Equal(2, result.Value.FacilitiesCreated); // facility shells still created
            }

            using (var carmenCtx = new AppDbContext(options, new FixedMunicipality(carmenId)))
            {
                Assert.Empty(await carmenCtx.Stalls.ToListAsync());
            }
        }

        [Fact]
        public async Task Activate_SeedsCustomAnimals_ScopedToLgu()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

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
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(config, default);
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
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

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
                var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(config, default);
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
            var (cantilanId, _, operatorId) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(code: "CANTILAN"), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task Activate_RejectsAlreadyActive()
        {
            var options = Options();
            var (cantilanId, _, operatorId) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var first = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(), default);
            Assert.True(first.IsSuccess);

            using var ctx2 = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var second = await new ActivateMunicipalityCommandHandler(ctx2, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(), default);
            Assert.False(second.IsSuccess);
        }

        [Fact]
        public async Task Activate_UnknownMunicipality_NotFound()
        {
            var options = Options();
            var (cantilanId, _, operatorId) = await SeedRegistryAsync(options);

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var result = await new ActivateMunicipalityCommandHandler(ctx, Operator(operatorId, cantilanId), Email, new IdentityPasswordHasher()).Handle(CarmenConfig(code: "NOWHERE"), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task Activate_NonPlatformOperator_Forbidden()
        {
            var options = Options();
            var (cantilanId, carmenId, operatorId) = await SeedRegistryAsync(options);

            // A municipality's own Head is not the platform operator - not Carmen's, and not the default LGU's either.
            using (var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result = await new ActivateMunicipalityCommandHandler(ctx, Head(carmenId), Email, new IdentityPasswordHasher())
                    .Handle(CarmenConfig(), default);
                Assert.False(result.IsSuccess);
            }

            // An Admin (not SuperAdmin) of the default LGU is likewise rejected.
            using (var ctx2 = new AppDbContext(options, new FixedMunicipality(cantilanId)))
            {
                var result2 = await new ActivateMunicipalityCommandHandler(ctx2, new FakeCurrentUser(Guid.NewGuid(), cantilanId, "Admin"), Email, new IdentityPasswordHasher())
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
