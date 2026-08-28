using EEMOCantilanSDS.Infrastructure.Security;
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
                var op = AdminUser.Create("Console Op", "console.op", "op@x.gov.ph", TestPasswords.Hash("Passw0rd!"), AdminRole.Admin, Guid.NewGuid(), isActive: true, isPlatformOperator: true);
                seed.AdminUsers.Add(op);
                await seed.SaveChangesAsync();
                opId = op.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var ok = await PlatformOperatorGuard.IsCurrentAsync(ctx, new FakeCurrentUser(opId, Guid.NewGuid(), "Admin"), default);
            Assert.True(ok);
        }

        [Fact]
        public async Task Guard_DefaultMunicipalitysHead_IsRefused()
        {
            // The retired fallback. The default municipality's Head is a municipal officer like any other now that a
            // dedicated operator account exists: no whole-database restore, no approving another LGU's onboarding.
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid headId;
            using (var seed = new AppDbContext(options))
            {
                var head = AdminUser.Create("Cantilan Head", "head", "head@x.gov.ph", TestPasswords.Hash("Passw0rd!"), AdminRole.SuperAdmin, cantilanId, isActive: true);
                seed.AdminUsers.Add(head);
                await seed.SaveChangesAsync();
                headId = head.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var ok = await PlatformOperatorGuard.IsCurrentAsync(ctx, new FakeCurrentUser(headId, cantilanId, "SuperAdmin"), default);
            Assert.False(ok);
        }

        [Fact]
        public async Task Guard_NonOperator_Fails()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid otherId;
            using (var seed = new AppDbContext(options))
            {
                var other = AdminUser.Create("Carmen Head", "carmen.head", "c@x.gov.ph", TestPasswords.Hash("Passw0rd!"), AdminRole.SuperAdmin, Guid.NewGuid(), isActive: true);
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
                var r = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), Moq.Mock.Of<IEmailVerificationSender>()).Handle(cmd, default);
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
                var r2 = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), Moq.Mock.Of<IEmailVerificationSender>()).Handle(cmd, default);
                Assert.False(r2.IsSuccess); // second run refused
                Assert.Equal(ResultStatus.Conflict, r2.Status);
                // A bare 409 carries no wording, so the console had to invent it — and it told the office
                // that setup was finished for every conflict, including the ones below.
                Assert.False(string.IsNullOrWhiteSpace(r2.Error));
                Assert.Contains("already exists", r2.Error!, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The e-mail address is subject to the same unique index as the username, and was never checked.
        ///
        /// <para>
        /// Reported from use: the office entered the address its own Head already holds. The insert reached
        /// Postgres, raised a unique violation, the middleware turned that into a 409, and the console reads
        /// any 409 as "a platform operator already exists" — so the operator was told setup was finished while
        /// the status endpoint kept correctly reporting that no operator existed at all.
        /// </para>
        /// </summary>
        [Fact]
        public async Task CreateFirstConsoleAdmin_EmailAlreadyUsed_SaysSo_AndIsNotAConflict()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);

            using (var seed = new AppDbContext(options))
            {
                seed.AdminUsers.Add(AdminUser.Create("Cantilan Head", "head", "office@cantilan.gov.ph",
                    TestPasswords.Hash("HeadPass1!"), AdminRole.SuperAdmin, cantilanId));
                await seed.SaveChangesAsync();
            }

            using var ctx = new AppDbContext(options);
            var result = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), Moq.Mock.Of<IEmailVerificationSender>())
                .Handle(new CreateFirstConsoleAdminCommand("Platform Operator", "console.admin", "office@cantilan.gov.ph", "Passw0rd1"), default);

            Assert.False(result.IsSuccess);
            // NOT a conflict: nothing about an operator existing is true, and calling it one is what misled.
            Assert.NotEqual(ResultStatus.Conflict, result.Status);
            Assert.Contains("e-mail", result.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("operator already exists", result.Error!, StringComparison.OrdinalIgnoreCase);

            // And no half-made account was left behind.
            Assert.False(await ctx.AdminUsers.IgnoreQueryFilters().AnyAsync(u => u.IsPlatformOperator));
        }

        /// <summary>
        /// The unique indexes on (MunicipalityId, Username) and (MunicipalityId, Email) are not filtered on
        /// soft-delete, so a removed account still holds both. The checks must read the database the same way,
        /// or the request passes them and fails in the insert with nothing the office can act on.
        /// </summary>
        [Fact]
        public async Task CreateFirstConsoleAdmin_ARemovedAccountStillHoldsItsUsernameAndEmail()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);

            using (var seed = new AppDbContext(options))
            {
                var removed = AdminUser.Create("Former Clerk", "console.admin", "former@cantilan.gov.ph",
                    TestPasswords.Hash("OldPass1!"), AdminRole.Admin, cantilanId);
                removed.SoftDelete("test");
                seed.AdminUsers.Add(removed);
                await seed.SaveChangesAsync();
            }

            using (var ctx = new AppDbContext(options))
            {
                var byUsername = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), Moq.Mock.Of<IEmailVerificationSender>())
                    .Handle(new CreateFirstConsoleAdminCommand("Platform Operator", "console.admin", "operator@stalltrack.site", "Passw0rd1"), default);
                Assert.False(byUsername.IsSuccess);
                Assert.Contains("username", byUsername.Error!, StringComparison.OrdinalIgnoreCase);
            }

            using (var ctx = new AppDbContext(options))
            {
                var byEmail = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), Moq.Mock.Of<IEmailVerificationSender>())
                    .Handle(new CreateFirstConsoleAdminCommand("Platform Operator", "operator", "former@cantilan.gov.ph", "Passw0rd1"), default);
                Assert.False(byEmail.IsSuccess);
                Assert.Contains("e-mail", byEmail.Error!, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>An address another municipality holds is no obstacle — the operator lives in the default one.</summary>
        [Fact]
        public async Task CreateFirstConsoleAdmin_AnAddressHeldByAnotherMunicipality_IsAccepted()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);

            using (var seed = new AppDbContext(options))
            {
                var madrid = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "madrid");
                seed.Municipalities.Add(madrid);
                await seed.SaveChangesAsync();

                seed.AdminUsers.Add(AdminUser.Create("Madrid Head", "madridhead", "shared@example.gov.ph",
                    TestPasswords.Hash("HeadPass1!"), AdminRole.SuperAdmin, madrid.Id));
                await seed.SaveChangesAsync();
            }

            using var ctx = new AppDbContext(options);
            var result = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), Moq.Mock.Of<IEmailVerificationSender>())
                .Handle(new CreateFirstConsoleAdminCommand("Platform Operator", "console.admin", "shared@example.gov.ph", "Passw0rd1"), default);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task TheOperatorIsAskedToConfirmItsAddress_SoItCanEverResetItsOwnPassword()
        {
            // A self-service reset is only ever sent to a VERIFIED address, and nothing verified this one. The operator —
            // the single account with nobody above it to restore its access — was therefore the only account on the
            // platform that could never reset its own password.
            var options = Options();
            await SeedDefaultAsync(options);

            var verification = new Moq.Mock<IEmailVerificationSender>();
            verification
                .Setup(v => v.SendAsync(Moq.It.IsAny<EEMOCantilanSDS.Domain.Entities.Users.BaseUser>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(true));

            using (var ctx = new AppDbContext(options))
            {
                var result = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), verification.Object)
                    .Handle(new CreateFirstConsoleAdminCommand("Platform Operator", "console.admin", "operator@stalltrack.site", "Passw0rd1"), default);

                Assert.True(result.IsSuccess);
            }

            // Asked of the account just created, not of some other user.
            verification.Verify(v => v.SendAsync(
                Moq.It.Is<EEMOCantilanSDS.Domain.Entities.Users.BaseUser>(u => u.Username == "console.admin"),
                Moq.It.IsAny<bool>(),
                Moq.It.IsAny<System.Threading.CancellationToken>()), Moq.Times.Once);
        }

        [Fact]
        public async Task AMailerThatFailsDoesNotFailThePlatformsSetup()
        {
            // The platform is being set up for the first time. An unconfigured or failing mailer must not leave it with
            // no operator at all: the account is created and saved before the email is attempted.
            var options = Options();
            await SeedDefaultAsync(options);

            var verification = new Moq.Mock<IEmailVerificationSender>();
            verification
                .Setup(v => v.SendAsync(Moq.It.IsAny<EEMOCantilanSDS.Domain.Entities.Users.BaseUser>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.FromResult(false));

            using (var ctx = new AppDbContext(options))
            {
                var result = await new CreateFirstConsoleAdminCommandHandler(ctx, new IdentityPasswordHasher(), verification.Object)
                    .Handle(new CreateFirstConsoleAdminCommand("Platform Operator", "console.admin", "operator@stalltrack.site", "Passw0rd1"), default);

                Assert.True(result.IsSuccess);
            }

            using var verify = new AppDbContext(options);
            Assert.True(await verify.AdminUsers.IgnoreQueryFilters().AnyAsync(u => u.IsPlatformOperator));
        }
    }
}
