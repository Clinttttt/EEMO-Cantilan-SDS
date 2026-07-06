using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetCurrentMunicipalityBranding;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Tenancy
{
    /// <summary>
    /// Authenticated "my LGU" branding: resolves the caller's municipality from the tenant context
    /// (the JWT municipality claim) and returns its office label/acronym + seal. Cantilan's acronym is
    /// "EEMO" — the same literal the current UI hardcodes — so binding it changes nothing for Cantilan.
    /// </summary>
    public class GetCurrentMunicipalityBrandingTests
    {
        private sealed class StubTenantContext(string tenantCode) : ITenantContext
        {
            public string TenantCode { get; } = tenantCode;
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        private static async Task SeedAsync(DbContextOptions<AppDbContext> options)
        {
            using var ctx = new AppDbContext(options);
            ctx.Municipalities.Add(Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
                tenantCode: "cantilan-sds",
                officeName: "Economic Enterprise and Management Office (EEMO)",
                officeAcronym: "EEMO", isDefault: true));
            ctx.Municipalities.Add(Municipality.Create(
                "CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Active,
                tenantCode: "carmen", officeName: "Carmen Economic Enterprise Office", officeAcronym: "CEEO"));
            await ctx.SaveChangesAsync();
        }

        [Fact]
        public async Task Resolves_Callers_Own_Lgu_With_Acronym()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetCurrentMunicipalityBrandingQueryHandler(
                new MunicipalityRepository(ctx), new StubTenantContext("cantilan-sds"))
                .Handle(new GetCurrentMunicipalityBrandingQuery(), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("CANTILAN", result.Value!.Code);
            Assert.Equal("EEMO", result.Value.OfficeAcronym);
        }

        [Fact]
        public async Task Resolves_Second_Lgu_By_Its_Own_Tenant()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetCurrentMunicipalityBrandingQueryHandler(
                new MunicipalityRepository(ctx), new StubTenantContext("carmen"))
                .Handle(new GetCurrentMunicipalityBrandingQuery(), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("CARMEN", result.Value!.Code);
            Assert.Equal("CEEO", result.Value.OfficeAcronym);
        }

        [Fact]
        public async Task Unresolvable_Tenant_NotFound()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetCurrentMunicipalityBrandingQueryHandler(
                new MunicipalityRepository(ctx), new StubTenantContext("nowhere"))
                .Handle(new GetCurrentMunicipalityBrandingQuery(), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(404, result.StatusCode);
        }
    }
}
