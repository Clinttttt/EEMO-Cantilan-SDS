using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Infrastructure.Auth
{
    /// <summary>
    /// Multi-LGU auth: (1) a non-default LGU user must be able to REFRESH — the refresh call is
    /// unauthenticated (default tenant) but the token is a global secret; (2) first-run Head setup is
    /// scoped to the DEFAULT (Cantilan) LGU, so activating other LGUs (which create their own Heads)
    /// never blocks Cantilan's setup.
    /// </summary>
    public class AuthTenantTests
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

        [Fact]
        public async Task ValidateRefreshToken_FindsUser_InAnotherTenant()
        {
            var options = Options();
            var carmen = Guid.NewGuid();
            const string raw = "raw-refresh-token-value";
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

            using (var seed = new AppDbContext(options, new FixedMunicipality(carmen)))
            {
                var head = AdminUser.Create("Carmen Head", "carmen.head", "h@carmen.gov", "Passw0rd!", AdminRole.SuperAdmin, carmen, isActive: true);
                head.SetRefreshToken(hash, DateTime.UtcNow.AddDays(7));
                seed.Users.Add(head);
                await seed.SaveChangesAsync();
            }

            // The refresh request resolves to a DIFFERENT (default) tenant; it must still find the Carmen user.
            using var ctx = new AppDbContext(options, new FixedMunicipality(Guid.NewGuid()));
            var svc = new TokenService(Mock.Of<IConfiguration>(), Mock.Of<IUnitOfWork>(), ctx);

            var user = await svc.ValidateRefreshToken(raw, CancellationToken.None);

            Assert.NotNull(user);
            Assert.Equal("carmen.head", user!.Username);
        }

        [Fact]
        public async Task FirstAdminSetup_IsRequired_EvenWhenAnotherLguHasASuperAdmin()
        {
            var options = Options();

            using var ctx = new AppDbContext(options);
            var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            var carmen = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "carmen");
            ctx.Municipalities.AddRange(cantilan, carmen);
            // Only Carmen (a non-default LGU) has a Head.
            ctx.Users.Add(AdminUser.Create("Carmen Head", "carmen.head", "h@carmen.gov", "Passw0rd!", AdminRole.SuperAdmin, carmen.Id, isActive: true));
            await ctx.SaveChangesAsync();

            var repo = new SetupRepository(ctx);

            // Cantilan has no Head yet → setup is STILL required (not blocked by Carmen's SuperAdmin).
            Assert.False(await repo.IsSuperAdminExistsAsync(CancellationToken.None));

            // A platform/console operator stamped to Cantilan (IsPlatformOperator) is NOT the Cantilan Head,
            // so setup must STILL be required — the console operator and the LGU Head are distinct identities.
            ctx.Users.Add(AdminUser.Create("Console Op", "console.op", "op@x.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan.Id, isActive: true, isPlatformOperator: true));
            await ctx.SaveChangesAsync();
            Assert.False(await repo.IsSuperAdminExistsAsync(CancellationToken.None));

            // Once Cantilan gets its (non-platform-operator) Head, setup is complete.
            ctx.Users.Add(AdminUser.Create("Cantilan Head", "cantilan.head", "h@cantilan.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan.Id, isActive: true));
            await ctx.SaveChangesAsync();
            Assert.True(await repo.IsSuperAdminExistsAsync(CancellationToken.None));
        }

        [Fact]
        public async Task CountOtherActiveSuperAdmins_ExcludesPlatformOperators()
        {
            // Regression: a platform/console operator carries the SuperAdmin role but is NOT the LGU's Head.
            // The demotion/deactivation guard must not count it, otherwise the last REAL Head could be demoted
            // — which then makes the first-run setup check (IsSuperAdminExistsAsync) report setup is required
            // again and the Head-setup screen reappears. This mirrors IsSuperAdminExistsAsync's exclusion.
            var options = Options();
            var cantilan = Guid.NewGuid();
            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilan));
            var head = AdminUser.Create("Cantilan Head", "head", "h@cantilan.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan, isActive: true);
            var op = AdminUser.Create("Console Op", "console.op", "op@x.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan, isActive: true, isPlatformOperator: true);
            ctx.Users.AddRange(head, op);
            await ctx.SaveChangesAsync();

            var repo = new AdminRepository(ctx);

            // Excluding the Head, the only other active SuperAdmin is the platform operator → must NOT count,
            // so demoting/deactivating the Head is correctly blocked (0 other real Heads remain).
            Assert.Equal(0, await repo.CountOtherActiveSuperAdminsAsync(head.Id, CancellationToken.None));

            // A second genuine Head IS counted.
            ctx.Users.Add(AdminUser.Create("Deputy Head", "deputy", "d@cantilan.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan, isActive: true));
            await ctx.SaveChangesAsync();
            Assert.Equal(1, await repo.CountOtherActiveSuperAdminsAsync(head.Id, CancellationToken.None));
        }

        [Fact]
        public async Task GetAllAdmins_ExcludesPlatformOperators()
        {
            // Regression: the account roster must not list the platform/console operator as an admin/Head of
            // the LGU — doing so showed a phantom second "Head" in Account Management.
            var options = Options();
            var cantilan = Guid.NewGuid();
            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilan));
            ctx.Users.Add(AdminUser.Create("Cantilan Head", "head", "h@cantilan.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan, isActive: true));
            ctx.Users.Add(AdminUser.Create("Console Op", "console.op", "op@x.gov", "Passw0rd!", AdminRole.SuperAdmin, cantilan, isActive: true, isPlatformOperator: true));
            await ctx.SaveChangesAsync();

            var repo = new AdminRepository(ctx);
            var admins = await repo.GetAllAsync(CancellationToken.None);

            Assert.Single(admins);
            Assert.Equal("head", admins[0].Username);
        }
    }
}
