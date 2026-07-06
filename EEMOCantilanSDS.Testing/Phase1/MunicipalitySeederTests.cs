using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Phase1;

/// <summary>
/// PHASE 1 — Municipality registry. Locks the seeded CARCANMADCARLAN state: Cantilan is the single
/// Active + default LGU; Carrascal/Madrid/Carmen/Lanuza are Upcoming; and seeding is idempotent.
/// </summary>
public class MunicipalitySeederTests : RepositoryTestBase
{
    [Fact]
    public async Task Seed_CreatesFiveLgus_CantilanActiveAndDefault_OthersUpcoming()
    {
        var context = NewContext();

        await MunicipalitySeeder.SeedAsync(context);

        var all = await context.Municipalities.ToListAsync();
        Assert.Equal(5, all.Count);
        Assert.Equal(
            new[] { "CANTILAN", "CARMEN", "CARRASCAL", "LANUZA", "MADRID" },
            all.Select(m => m.Code).OrderBy(c => c).ToArray());

        var cantilan = all.Single(m => m.Code == "CANTILAN");
        Assert.Equal(MunicipalityStatus.Active, cantilan.Status);
        Assert.True(cantilan.IsDefault);
        Assert.True(cantilan.IsActive);
        Assert.Equal("Surigao del Sur", cantilan.Province);
        Assert.Equal("EEMO", cantilan.OfficeAcronym);
        Assert.Contains("Economic Enterprise", cantilan.OfficeName);

        // Exactly one default LGU.
        Assert.Single(all, m => m.IsDefault);

        // Each LGU has a distinct, stable cache namespace (TenantCode). Cantilan == the live
        // DefaultTenantCode so its cache/claim are byte-for-byte unchanged; the rest are distinct.
        Assert.Equal("cantilan-sds", cantilan.TenantCode);
        Assert.Equal("carrascal", all.Single(m => m.Code == "CARRASCAL").TenantCode);
        Assert.Equal("madrid", all.Single(m => m.Code == "MADRID").TenantCode);
        Assert.Equal("carmen", all.Single(m => m.Code == "CARMEN").TenantCode);
        Assert.Equal("lanuza", all.Single(m => m.Code == "LANUZA").TenantCode);
        // All TenantCodes are unique (no cross-tenant cache collision).
        Assert.Equal(all.Count, all.Select(m => m.TenantCode).Distinct().Count());

        // Every other LGU is an Upcoming, non-default rollout slot.
        Assert.All(all.Where(m => m.Code != "CANTILAN"), m =>
        {
            Assert.Equal(MunicipalityStatus.Upcoming, m.Status);
            Assert.False(m.IsDefault);
            Assert.False(m.IsActive);
            Assert.Equal("Surigao del Sur", m.Province);
        });
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        var context = NewContext();

        await MunicipalitySeeder.SeedAsync(context);
        await MunicipalitySeeder.SeedAsync(context);

        Assert.Equal(5, await context.Municipalities.CountAsync());
    }
}
