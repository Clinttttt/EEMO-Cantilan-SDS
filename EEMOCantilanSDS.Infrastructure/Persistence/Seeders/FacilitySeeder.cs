using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Seeders;

public static class FacilitySeeder
{
    public static async Task SeedAsync(IAppDbContext context)
    {
        var exists = await context.Facilities.IgnoreQueryFilters().AnyAsync();
        if (exists) { return; }

        // Attribute the seeded facilities to the default municipality (Cantilan). Requires the
        // MunicipalitySeeder to have run first; if there's no default yet, skip rather than create
        // orphaned (empty-tenant) rows that the per-LGU query filter would hide from the dashboard.
        var municipalityId = await context.Municipalities
            .IgnoreQueryFilters()
            .Where(m => m.IsDefault)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        if (municipalityId == Guid.Empty) { return; }

        var facilities = new[]
        {
            Facility.Create(
                FacilityCode.NPM,
                "New Public Market",
                "NPM",
                "Daily rental market with vegetable, fish, and meat sections. ₱30/day + utilities + ₱1/kg fish fee.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.TCC,
                "Tampak Commercial Center",
                "TCC",
                "Monthly rental commercial stalls. Rate range: ₱2,400 - ₱4,800/month.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.NCC,
                "New Commercial Center",
                "NCC",
                "Monthly rental with Extension (₱1,200) and Corner (₱3,240-₱3,840) areas.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.BBQ,
                "Barbecue Stand",
                "BBQ",
                "Space rental for barbecue vendors. Rate range: ₱1,600 - ₱9,600/month.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.ICE,
                "Iceplant",
                "ICE",
                "Ice storage and distribution facility. Rate range: ₱1,000 - ₱2,000/month.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.SLH,
                "Slaughterhouse",
                "SLH",
                "Per-head slaughter fees. Hog: ₱250/head, Large animals (Carabao/Cow): ₱365/head.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.TRM,
                "Transport Terminal",
                "TRM",
                "Municipal transport terminal managing driver departures. ₱30 per trip.",
                municipalityId: municipalityId),

            Facility.Create(
                FacilityCode.TPM,
                "Tabo-an Public Market",
                "TPM",
                "Weekly public market held every Friday. ₱100 per vendor per market day.",
                municipalityId: municipalityId)
        };

        await context.Facilities.AddRangeAsync(facilities);
        await context.SaveChangesAsync();
    }
}
