using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Arrears are counted only for months that have ENDED, so the count depends on the day the question is asked.
///
/// <para>
/// The office's rule: a payor is not in arrears for a month they can still pay, and certainly not for a month that has not
/// arrived. The yearly view offers every month of the year, so an anchor can legitimately be in the future and has to be
/// clamped to the last month that actually closed.
/// </para>
///
/// <para>
/// This is the reporting arithmetic that produced a real, reported defect once before: the same debt was stated as ₱9,900 by
/// the Financial Reports and ₱33,300 by the register, and the smaller figure is the one that reaches a demand letter. Being
/// able to ask "what would this say on such a date" is how that class of disagreement is caught.
/// </para>
/// </summary>
public class FacilityReportsRepositoryClockTests : RepositoryTestBase
{
    private static FacilityReportsRepository RepositoryAsOf(
        EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context, DateOnly today)
    {
        IClock clock = new FixedClock(today.ToDateTime(TimeOnly.MinValue).AddHours(-8));   // 00:00 Philippine time
        return new FacilityReportsRepository(context, new FeeRateResolver(context), clock);
    }

    /// <summary>A monthly-billed stall let from the first of January 2026, with nothing ever collected.</summary>
    private async Task<EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext> SeedUnpaidStallAsync()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tourism and Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", 2_400m, ApplicableFees.None);
        var contract = Contract.Create(stall.Id, "Bernadette Lim", "Bernadette Lim", new DateOnly(2026, 1, 1), 3, 2_400m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();
        return context;
    }

    [Theory]
    // Asked in April about April: January, February and March have closed — three months missed.
    [InlineData(4, 4, 3)]
    // Asked in April about DECEMBER: still three, because the clamp stops at the last month that ended. Without it the
    // yearly view would report months nobody has reached yet.
    [InlineData(4, 12, 3)]
    // Asked in July about July: six closed months.
    [InlineData(7, 7, 6)]
    // Asked in February about February: only January has closed.
    [InlineData(2, 2, 1)]
    public async Task ArrearsCountOnlyMonthsThatHaveEnded(int todayMonth, int askedMonth, int expectedUnpaid)
    {
        var context = await SeedUnpaidStallAsync();

        var rows = await RepositoryAsOf(context, new DateOnly(2026, todayMonth, 15))
            .GetDelinquentStallsAsync(FacilityCode.TCC, 2026, askedMonth, includeClosed: false, wholeAccount: true, CancellationToken.None);

        var stall = Assert.Single(rows);
        Assert.Equal(expectedUnpaid, stall.MonthsUnpaid);
    }

    [Fact]
    public async Task TheMonthInProgressIsNeverCountedAsMissed()
    {
        // The same account read on the last day of March and the first day of April. Nothing was paid in between; the only
        // difference is that March ended, so the count goes up by exactly one.
        var context = await SeedUnpaidStallAsync();

        var onMarch31 = await RepositoryAsOf(context, new DateOnly(2026, 3, 31))
            .GetDelinquentStallsAsync(FacilityCode.TCC, 2026, 3, includeClosed: false, wholeAccount: true, CancellationToken.None);
        var onApril1 = await RepositoryAsOf(context, new DateOnly(2026, 4, 1))
            .GetDelinquentStallsAsync(FacilityCode.TCC, 2026, 4, includeClosed: false, wholeAccount: true, CancellationToken.None);

        Assert.Equal(2, Assert.Single(onMarch31).MonthsUnpaid);   // January and February
        Assert.Equal(3, Assert.Single(onApril1).MonthsUnpaid);    // and now March
    }
}
