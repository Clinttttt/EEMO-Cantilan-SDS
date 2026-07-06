using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalityBranding;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Tenancy
{
    /// <summary>
    /// Public pre-login branding lookup: resolves a single LGU by subdomain identifier (TenantCode or Code),
    /// case-insensitively, and returns only public-safe presentation fields.
    /// </summary>
    public class GetMunicipalityBrandingTests
    {
        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
        {
            using var ctx = new AppDbContext(options);
            ctx.Municipalities.Add(Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
                tenantCode: "cantilan-sds", officeName: "EEMO Cantilan", sealPath: "/seals/cantilan.png", isDefault: true));
            ctx.Municipalities.Add(Municipality.Create(
                "CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming,
                tenantCode: "carmen", officeName: "Carmen EEO", sealPath: "/seals/carmen.png"));
            await ctx.SaveChangesAsync();
        }

        [Theory]
        [InlineData("carmen")]   // by tenant code
        [InlineData("CARMEN")]   // by registry code
        [InlineData("Carmen")]   // case-insensitive
        public async Task Resolves_Carmen_ByIdentifier(string identifier)
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetMunicipalityBrandingQueryHandler(new MunicipalityRepository(ctx))
                .Handle(new GetMunicipalityBrandingQuery(identifier), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("CARMEN", result.Value!.Code);
            Assert.Equal("carmen", result.Value.TenantCode);
            Assert.Equal("Carmen EEO", result.Value.OfficeName);
            Assert.Equal("/seals/carmen.png", result.Value.SealPath);
            Assert.Equal("Upcoming", result.Value.Status);
            Assert.False(result.Value.IsActive);
        }

        [Fact]
        public async Task Resolves_Cantilan_ByTenantCode_CaseInsensitive()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetMunicipalityBrandingQueryHandler(new MunicipalityRepository(ctx))
                .Handle(new GetMunicipalityBrandingQuery("CANTILAN-SDS"), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("CANTILAN", result.Value!.Code);
            Assert.True(result.Value.IsActive);
        }

        [Fact]
        public async Task UnknownIdentifier_NotFound()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetMunicipalityBrandingQueryHandler(new MunicipalityRepository(ctx))
                .Handle(new GetMunicipalityBrandingQuery("nowhere"), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(404, result.StatusCode);
        }
    }
}
