using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.CreateFirstConsoleAdmin;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetPlatformSetupStatus;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Onboarding
{
    /// <summary>
    /// Dedicated platform/console operator: the IsPlatformOperator flag authorizes onboarding independent of
    /// a municipality's Head, with the default-SuperAdmin fallback kept for backward compatibility. First-run
    /// creation self-disables once an operator exists.
    /// </summary>
    public class ConsoleAdminHandlerTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        private sealed class FakeCurrentUser(Guid? userId, Guid? municipalityId, string? role) : ICurrentUserService
        {
            public bool IsAuthenticated => true;
            public Guid? UserId => userId;
            public string? Username => "user";
            public string? Role => role;
            public Guid? CollectorId => null;
            public string? MunicipalityCode => null;
            public Guid? MunicipalityId => municipalityId;
            public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private static async Task<Guid> SeedDefaultAsync(DbContextOptions<AppDbContext> options)
        {
            using var seed = new AppDbContext(options);
            var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            seed.Municipalities.Add(cantilan);
            await seed.SaveChangesAsync();
            return cantilan.Id;
        }

        [Fact]
        public async Task Guard_PlatformOperatorFlag_Passes_RegardlessOfRoleOrMunicipality()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid opId;
            using (var seed = new AppDbContext(options))
            {
                // A dedicated operator that is NOT a default-LGU SuperAdmin (role Admin, different muni).
                var op = AdminUser.Create("Console Op", "console.op", "op@x.gov.ph", "Passw0rd!", AdminRole.Admin, Guid.NewGuid(), isActive: true, isPlatformOperator: true);
                seed.AdminUsers.Add(op);
                await seed.SaveChangesAsync();
                opId = op.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var ok = await PlatformOperatorGuard.IsCurrentAsync(ctx, new FakeCurrentUser(opId, Guid.NewGuid(), "Admin"), default);
            Assert.True(ok);
        }

        [Fact]
        public async Task Guard_Fallback_DefaultSuperAdmin_StillPasses()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid headId;
            using (var seed = new AppDbContext(options))
            {
                var head = AdminUser.Create("Cantilan Head", "head", "head@x.gov.ph", "Passw0rd!", AdminRole.SuperAdmin, cantilanId, isActive: true);
                seed.AdminUsers.Add(head);
                await seed.SaveChangesAsync();
                headId = head.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var ok = await PlatformOperatorGuard.IsCurrentAsync(ctx, new FakeCurrentUser(headId, cantilanId, "SuperAdmin"), default);
            Assert.True(ok);
        }

        [Fact]
        public async Task Guard_NonOperator_Fails()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid otherId;
            using (var seed = new AppDbContext(options))
            {
                var other = AdminUser.Create("Carmen Head", "carmen.head", "c@x.gov.ph", "Passw0rd!", AdminRole.SuperAdmin, Guid.NewGuid(), isActive: true);
                seed.AdminUsers.Add(other);
                await seed.SaveChangesAsync();
                otherId = other.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            // SuperAdmin of a NON-default municipality, no flag → not an operator.
            var ok = await PlatformOperatorGuard.IsCurrentAsync(ctx, new FakeCurrentUser(otherId, Guid.NewGuid(), "SuperAdmin"), default);
            Assert.False(ok);
        }

        [Fact]
        public async Task CreateFirstConsoleAdmin_Creates_Then_Conflicts_AndSetupStatusFlips()
        {
            var options = Options();
            await SeedDefaultAsync(options);

            using (var ctx = new AppDbContext(options))
            {
                var status = await new GetPlatformSetupStatusQueryHandler(ctx).Handle(new GetPlatformSetupStatusQuery(), default);
                Assert.True(status.Value!.IsSetupRequired);
            }

            var cmd = new CreateFirstConsoleAdminCommand("Platform Operator", "console.admin", "console@stalltrack.site", "Passw0rd1");
            using (var ctx = new AppDbContext(options))
            {
                var r = await new CreateFirstConsoleAdminCommandHandler(ctx).Handle(cmd, default);
                Assert.True(r.IsSuccess);
            }
            using (var ctx = new AppDbContext(options))
            {
                Assert.True(await ctx.AdminUsers.IgnoreQueryFilters().AnyAsync(u => u.IsPlatformOperator));
                var status = await new GetPlatformSetupStatusQueryHandler(ctx).Handle(new GetPlatformSetupStatusQuery(), default);
                Assert.False(status.Value!.IsSetupRequired);
            }
            using (var ctx = new AppDbContext(options))
            {
                var r2 = await new CreateFirstConsoleAdminCommandHandler(ctx).Handle(cmd, default);
                Assert.False(r2.IsSuccess); // second run refused
            }
        }
    }
}
