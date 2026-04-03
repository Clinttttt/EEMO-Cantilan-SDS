using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Seeders;

public static class FacilitySeeder
{
    public static async Task SeedAsync(IAppDbContext context)
    {
        var exists = await context.Facilities.AnyAsync();
        if (exists) { return; }

        var facilities = new[]
        {
            Facility.Create(
                FacilityCode.NPM,
                "New Public Market",
                "NPM",
                "Daily rental market with vegetable, fish, and meat sections. ₱30/day + utilities + ₱1/kg fish fee."),

            Facility.Create(
                FacilityCode.TCC,
                "Tampak Commercial Center",
                "TCC",
                "Monthly rental commercial stalls. Rate range: ₱2,400 - ₱4,800/month."),

            Facility.Create(
                FacilityCode.NCC,
                "New Commercial Center",
                "NCC",
                "Monthly rental with Extension (₱1,200) and Corner (₱3,240-₱3,840) areas."),

            Facility.Create(
                FacilityCode.BBQ,
                "Barbecue Stand",
                "BBQ",
                "Space rental for barbecue vendors. Rate range: ₱1,600 - ₱9,600/month."),

            Facility.Create(
                FacilityCode.ICE,
                "Iceplant",
                "ICE",
                "Ice storage and distribution facility. Rate range: ₱1,000 - ₱2,000/month."),

            Facility.Create(
                FacilityCode.SLH,
                "Slaughterhouse",
                "SLH",
                "Per-head slaughter fees. Hog: ₱250/head, Large animals (Carabao/Cow): ₱365/head.")
        };

        await context.Facilities.AddRangeAsync(facilities);
        await context.SaveChangesAsync();
    }
}
