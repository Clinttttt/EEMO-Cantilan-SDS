using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Onboarding
{
    /// <summary>
    /// The Head's account-activation link: a one-time, hashed, expiring token that lets a provisioned
    /// (inactive) account set its own password and go active. Proves valid activation + single-use, and that
    /// invalid/expired tokens are rejected without activating.
    /// </summary>
    public class SetAdminPasswordByTokenCommandHandlerTests
    {
        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

        private static async Task<Guid> SeedInactiveHeadWithToken(DbContextOptions<AppDbContext> options, string rawToken, DateTime expiry)
        {
            using var seed = new AppDbContext(options);
            var head = AdminUser.Create("Maria", "carmen.head", "head@carmen.gov.ph", "placeholder", AdminRole.SuperAdmin, Guid.NewGuid(), isActive: false);
            head.SetActivationToken(Hash(rawToken), expiry);
            seed.AdminUsers.Add(head);
            await seed.SaveChangesAsync();
            return head.Id;
        }

        [Fact]
        public async Task ValidToken_ActivatesAndSetsPassword_SingleUse()
        {
            var options = Options();
            var id = await SeedInactiveHeadWithToken(options, "tok-123", DateTime.UtcNow.AddDays(7));

            using (var ctx = new AppDbContext(options))
            {
                var result = await new SetAdminPasswordByTokenCommandHandler(ctx)
                    .Handle(new SetAdminPasswordByTokenCommand("tok-123", "NewPass123"), default);
                Assert.True(result.IsSuccess);
            }

            using (var verify = new AppDbContext(options))
            {
                var head = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
                Assert.True(head.IsActive);
                Assert.False(head.MustChangePassword);
                Assert.Null(head.ActivationTokenHash);
                Assert.True(head.VerifyPassword("NewPass123"));
            }

            // Single-use: the same token no longer works.
            using (var ctx = new AppDbContext(options))
            {
                var again = await new SetAdminPasswordByTokenCommandHandler(ctx)
                    .Handle(new SetAdminPasswordByTokenCommand("tok-123", "Another123"), default);
                Assert.False(again.IsSuccess);
            }
        }

        [Fact]
        public async Task InvalidToken_Fails()
        {
            var options = Options();
            await SeedInactiveHeadWithToken(options, "tok-123", DateTime.UtcNow.AddDays(7));

            using var ctx = new AppDbContext(options);
            var result = await new SetAdminPasswordByTokenCommandHandler(ctx)
                .Handle(new SetAdminPasswordByTokenCommand("wrong-token", "NewPass123"), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task ExpiredToken_Fails_AndAccountStaysInactive()
        {
            var options = Options();
            await SeedInactiveHeadWithToken(options, "tok-123", DateTime.UtcNow.AddDays(-1));

            using var ctx = new AppDbContext(options);
            var result = await new SetAdminPasswordByTokenCommandHandler(ctx)
                .Handle(new SetAdminPasswordByTokenCommand("tok-123", "NewPass123"), default);

            Assert.False(result.IsSuccess);
            var head = await ctx.AdminUsers.IgnoreQueryFilters().FirstAsync();
            Assert.False(head.IsActive);
        }
    }
}
