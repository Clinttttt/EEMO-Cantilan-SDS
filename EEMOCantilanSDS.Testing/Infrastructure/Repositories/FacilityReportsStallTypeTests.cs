using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Status Report's "Stall Type" column is backed by the real Stall.Type field
/// (no longer a hardcoded "Permanent"), surfaced through StallComplianceDto.StallType.
/// </summary>
public class FacilityReportsStallTypeTests : RepositoryTestBase
{
    [Fact]
    public async Task StallCompliance_DefaultsToPermanent_WhenTypeNotSet()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Equal("Permanent", report.StallCompliance.Single().StallType);
    }

    [Fact]
    public async Task StallCompliance_ReflectsTransientType_WhenSet()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental,
            section: MarketSection.FishSection, type: StallType.Transient);
        var contract = Contract.Create(stall.Id, "Lorna Buenades", "Lorna Buenades", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Equal("Transient", report.StallCompliance.Single().StallType);
    }
}
