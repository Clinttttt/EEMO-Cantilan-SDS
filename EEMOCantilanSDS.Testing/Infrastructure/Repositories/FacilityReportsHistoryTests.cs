using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The History report reuses the per-period aggregation, so its rows must match exactly what the
/// Monthly/Yearly report shows, and it must not fabricate future months.
/// </summary>
public class FacilityReportsHistoryTests : RepositoryTestBase
{
    [Fact]
    public async Task History_MonthlyRows_MatchPerMonthReport_AndCoverFullPastYear()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2024, 1, 1), 5, 900m);
        var payment = PaymentRecord.Create(stall.Id, 2024, 3, 900m);
        payment.UpdateStatus(PaymentStatus.Partial, 500m);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);

        // 2024 is a full past year → all 12 months present, plus a rolling 5-year summary.
        var history = await repo.GetFacilityHistoryAsync(FacilityCode.NPM, 2024, CancellationToken.None);
        Assert.Equal(12, history.Monthly.Count);
        Assert.Equal(5, history.Yearly.Count);

        // March 2024 row must equal the standalone monthly report for March 2024.
        var march = history.Monthly.Single(m => m.Month == 3);
        var marchReport = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2024, 3, null, CancellationToken.None);
        Assert.Equal(marchReport.TotalRevenue, march.Collected);
        Assert.Equal(marchReport.PendingPaymentAmount, march.Outstanding);
        Assert.Equal(marchReport.PendingPaymentCount, march.FollowUp);
        Assert.Equal(marchReport.OccupiedStalls, march.TotalStalls);
    }

    [Fact]
    public async Task History_CurrentYear_OnlyIncludesMonthsThatHaveStarted()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        context.Add(facility);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var today = PhilippineTime.Today;
        var history = await repo.GetFacilityHistoryAsync(FacilityCode.NPM, today.Year, CancellationToken.None);

        Assert.Equal(today.Month, history.Monthly.Count);
        Assert.All(history.Monthly, m => Assert.True(m.Month <= today.Month));
    }
}
