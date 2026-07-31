using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A stall handed to a new lessee must still report a PAST period under the lessee who held it then. Before this,
/// compliance rows were built from the stall's currently-active contract, so a 2025 report showed the 2026 lessee's
/// name — and the previous lessee's own row disappeared once their contract was terminated.
/// </summary>
public class FacilityReportsOccupancyAttributionTests : RepositoryTestBase
{
    private static Contract Term(Guid stallId, string occupant, DateOnly from, int years, decimal rate) =>
        Contract.Create(stallId, occupant, occupant, from, years, rate);

    private static FacilityReportsRepository NewReportsRepository(EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context) =>
        new(context);

    [Fact]
    public async Task APastPeriod_ReportsTheLesseeWhoHeldTheStallThen()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_000m, ApplicableFees.BaseRental);

        // Wilma held it through 2025 and was handed over on 30 Jun 2026; Teofila took it on 1 Jul 2026.
        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2024, 1, 1), 3, 1_000m);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 30));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 7, 1), 3, 1_200m);

        // A 2025 month, paid by Wilma.
        var may2025 = PaymentRecord.Create(stall.Id, 2025, 5, 1_000m);
        may2025.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, outgoing, incoming, may2025);
        await context.SaveChangesAsync();

        var report = await NewReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2025, 5, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal("3", row.StallNo);
        Assert.Equal("Wilma K. Tecson", row.Occupant);      // not the lessee who arrived in 2026
        Assert.Equal(1_000m, row.MonthlyRate);              // and at HER rate, not the new one's ₱1,200
    }

    [Fact]
    public async Task TheCurrentPeriod_StillReportsTheSittingLessee()
    {
        // The other half of the rule: today's numbers must be unchanged by all of this.
        var context = NewContext();
        var today = PhilippineTime.Today;
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_200m, ApplicableFees.BaseRental);

        var outgoing = Term(stall.Id, "Wilma K. Tecson", today.AddYears(-3), 2, 1_000m);
        outgoing.Terminate("Head", today.AddMonths(-2));
        var incoming = Term(stall.Id, "Teofila Reyes", today.AddMonths(-1), 3, 1_200m);

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var report = await NewReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, today.Year, today.Month, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal("Teofila Reyes", row.Occupant);
        Assert.Equal(1_200m, row.MonthlyRate);
    }
}
