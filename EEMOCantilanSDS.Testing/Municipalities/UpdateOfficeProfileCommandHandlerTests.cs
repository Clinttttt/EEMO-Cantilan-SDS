using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Testing.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Municipalities
{
    /// <summary>
    /// Self-service branding: an LGU Head updates their own municipality's office profile. Scoped to the
    /// caller's LGU (from the token), never touches another LGU, and requires a municipality claim.
    /// </summary>
    public class UpdateOfficeProfileCommandHandlerTests
    {
        private sealed class FakeCurrentUser(Guid? municipalityId) : ICurrentUserService
        {
            public bool IsAuthenticated => true;
            public Guid? UserId => Guid.NewGuid();
            public string? Username => "head";
            public string? Role => "SuperAdmin";
            public Guid? CollectorId => null;
            public string? MunicipalityCode => null;
            public Guid? MunicipalityId => municipalityId;
            public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        private static async Task<(Guid a, Guid b)> SeedAsync(DbContextOptions<AppDbContext> options)
        {
            using var seed = new AppDbContext(options);
            var a = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "carmen", officeName: "Old Office");
            var b = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "madrid", officeName: "Madrid Office");
            seed.Municipalities.Add(a);
            seed.Municipalities.Add(b);
            await seed.SaveChangesAsync();
            return (a.Id, b.Id);
        }

        [Fact]
        public async Task Updates_Own_Lgu_Only()
        {
            var options = Options();
            var (a, b) = await SeedAsync(options);

            using (var ctx = new AppDbContext(options))
            {
                var result = await new UpdateOfficeProfileCommandHandler(
                        ctx, new FakeCurrentUser(a), CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
                    .Handle(new UpdateOfficeProfileCommand("Carmen Treasury Office", "Carmen, SDS", null), default);
                Assert.True(result.IsSuccess);
            }

            using (var verify = new AppDbContext(options))
            {
                var carmen = await verify.Municipalities.IgnoreQueryFilters().FirstAsync(m => m.Id == a);
                var madrid = await verify.Municipalities.IgnoreQueryFilters().FirstAsync(m => m.Id == b);
                Assert.Equal("Carmen Treasury Office", carmen.OfficeName);
                Assert.Equal("Carmen, SDS", carmen.Address);
                Assert.Equal("Madrid Office", madrid.OfficeName); // untouched
            }
        }

        [Fact]
        public async Task NoMunicipalityClaim_Forbidden()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new UpdateOfficeProfileCommandHandler(
                    ctx, new FakeCurrentUser(null), CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
                .Handle(new UpdateOfficeProfileCommand("X", null, null), default);

            Assert.False(result.IsSuccess);
        }
    }
}
