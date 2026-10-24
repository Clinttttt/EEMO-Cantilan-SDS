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
    /// "MEEDO" (Municipal Economic Enterprises Development Office).
    /// </summary>
    public class GetCurrentMunicipalityBrandingTests
    {
        private sealed class StubMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
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
                officeName: "Municipal Economic Enterprises Development Office",
                officeAcronym: "MEEDO", isDefault: true));
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
                new MunicipalityRepository(ctx), new StubMunicipality((await ctx.Municipalities.SingleAsync(x => x.Code == "CANTILAN")).Id))
                .Handle(new GetCurrentMunicipalityBrandingQuery(), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("CANTILAN", result.Value!.Code);
            Assert.Equal("MEEDO", result.Value.OfficeAcronym);
        }

        [Fact]
        public async Task Resolves_Second_Lgu_By_Its_Own_Tenant()
        {
            var options = Options();
            await SeedAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetCurrentMunicipalityBrandingQueryHandler(
                new MunicipalityRepository(ctx), new StubMunicipality((await ctx.Municipalities.SingleAsync(x => x.Code == "CARMEN")).Id))
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
                new MunicipalityRepository(ctx), new StubMunicipality(Guid.NewGuid()))
                .Handle(new GetCurrentMunicipalityBrandingQuery(), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(ResultStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task UnresolvedAuthenticatedTenantNeverFallsBackToTheDefault()
        {
            var options = Options();
            await SeedAsync(options);
            using var ctx = new AppDbContext(options);
            var result = await new GetCurrentMunicipalityBrandingQueryHandler(
                new MunicipalityRepository(ctx), new StubMunicipality(Guid.Empty))
                .Handle(new GetCurrentMunicipalityBrandingQuery(), default);
            Assert.Equal(ResultStatus.Forbidden, result.Status);
            Assert.Null(result.Value);
        }
    }
}
