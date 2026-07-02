using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds the CARCANMADCARLAN municipality registry. Cantilan is the live/default implementation;
/// the other four are future-ready rollout slots (Upcoming) until validated and onboarded.
/// Idempotent — does nothing if any municipality already exists.
/// </summary>
public static class MunicipalitySeeder
{
    public static async Task SeedAsync(IAppDbContext context)
    {
        if (await context.Municipalities.AnyAsync()) { return; }

        var municipalities = new[]
        {
            Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
                officeName: "Economic Enterprise and Management Office (EEMO)",
                isDefault: true),

            Municipality.Create("CARRASCAL", "Carrascal", "Surigao del Sur", MunicipalityStatus.Upcoming),
            Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Upcoming),
            Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming),
            Municipality.Create("LANUZA", "Lanuza", "Surigao del Sur", MunicipalityStatus.Upcoming),
        };

        await context.Municipalities.AddRangeAsync(municipalities);
        await context.SaveChangesAsync();
    }
}
