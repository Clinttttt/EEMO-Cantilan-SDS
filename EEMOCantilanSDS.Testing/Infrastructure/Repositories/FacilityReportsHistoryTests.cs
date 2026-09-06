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

    /// <summary>
    /// A year's outstanding is the sum of its months, which is what lets the report total that column.
    /// </summary>
    /// <remarks>
    /// Reported from use on the New Public Market history: August showed ₱1,140.00 outstanding and September ₱840.00, and the Total
    /// row said ₱1,140.00 - the LARGER of the two rather than the ₱1,980.00 owed. Six report pages totalled that column with
    /// <c>Max</c> while totalling Collected beside it with <c>Sum</c>, so the row contradicted itself and understated what the office
    /// is still owed.
    ///
    /// <para>Summing is correct because each monthly row is that month ALONE: <c>CurrentAccountStart</c> bounds the obligation below
    /// by the period start, and the balance is period obligation minus period collections - so the months are disjoint and add up.
    /// This test states that as a property rather than an arithmetic example, by checking the months against the YEAR the same code
    /// computes over the whole range. If ever the monthly figures become cumulative instead, this fails and the total must change with
    /// them.</para>
    /// </remarks>
    [Fact]
    public async Task History_MonthlyOutstanding_AddsUpToTheYearsOwnFigure()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2024, 1, 1), 5, 900m);

        // One partial payment and nothing else, so most of the year stays owed and several months carry a balance.
        var payment = PaymentRecord.Create(stall.Id, 2024, 3, 900m);
        payment.UpdateStatus(PaymentStatus.Partial, 500m);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var history = await repo.GetFacilityHistoryAsync(FacilityCode.NPM, 2024, CancellationToken.None);

        var monthsOwing = history.Monthly.Count(m => m.Outstanding > 0m);
        Assert.True(monthsOwing >= 2,
            $"This test only means something when more than one month is owed; {monthsOwing} were. Fix the seed, not the assertion.");

        var year = history.Yearly.Single(y => y.Label == "2024");
        Assert.Equal(year.Outstanding, history.Monthly.Sum(m => m.Outstanding));

        // Named plainly, because Max was the bug: the largest single month is NOT the year's outstanding.
        Assert.True(history.Monthly.Sum(m => m.Outstanding) > history.Monthly.Max(m => m.Outstanding),
            "With several months owed, the year's outstanding must exceed the worst single month.");
    }
}
