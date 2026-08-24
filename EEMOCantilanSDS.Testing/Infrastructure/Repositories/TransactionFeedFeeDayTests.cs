using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// A recorded collection says which DAY its fee answers for, when that is not the day it was recorded.
///
/// Reported 2026-08-24: the office read three collections on the Transactions page, all recorded that day, while the
/// collector's own app showed two of those stalls as still owing that day. Both screens were right and neither was wrong
/// — the page lists what was RECORDED today whatever day each fee is for, and the app shows what TODAY owes — but a row
/// reading only "Stall 1" was taken as today's fee, so the two screens appeared to contradict each other about one stall.
///
/// A multi-day settlement already stated its fee period. A single day did not, which is the whole gap.
/// </summary>
public class TransactionFeedFeeDayTests : RepositoryTestBase
{
    private static (Facility Facility, Stall Stall, Contract Contract) NpmStall(string stallNo)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, stallNo, 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(
            stall.Id, "Kim Chui", "Kim Chui", PhilippineTime.Today.AddMonths(-2), 3, 900m);
        return (facility, stall, contract);
    }

    private static DailyCollection PaidOn(Guid stallId, DateOnly feeDay, string or)
    {
        var dc = DailyCollection.Create(stallId, feeDay, "collector1", 30m);
        dc.MarkPaid(or, collectorId: null, fishKilos: null, updatedBy: "collector1");
        return dc;
    }

    [Fact]
    public async Task ADayCollectedOnItsOwnDayReadsExactlyAsBefore()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();
        var (facility, stall, contract) = NpmStall("1");
        context.AddRange(facility, stall, contract, PaidOn(stall.Id, today, "OR-1"));
        await context.SaveChangesAsync();

        var rows = await new TransactionFeedRepository(context, new EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(context), new SystemClock()).GetRecentTransactionsAsync(FacilityCode.NPM, today, 50, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Stall 1", row.Reference);
    }

    [Fact]
    public async Task ADayCollectedLaterSaysWhichDayItIsFor()
    {
        // The reported shape: an older day settled today. The office can now see that this row is not today's fee, which
        // is why the collector's app still shows the stall as owing today.
        var today = PhilippineTime.Today;
        var context = NewContext();
        var (facility, stall, contract) = NpmStall("2");
        context.AddRange(facility, stall, contract, PaidOn(stall.Id, today.AddDays(-2), "OR-2"));
        await context.SaveChangesAsync();

        var rows = await new TransactionFeedRepository(context, new EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(context), new SystemClock()).GetRecentTransactionsAsync(FacilityCode.NPM, today, 50, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Contains("Stall 2", row.Reference);
        Assert.Contains("for " + today.AddDays(-2).ToString("MMM d"), row.Reference);
    }

    [Fact]
    public async Task AMultiDaySettlementStillStatesItsFeePeriod()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();
        var (facility, stall, contract) = NpmStall("3");
        // Two days under one receipt: one feed row, and it names the period rather than a single day.
        context.AddRange(
            facility, stall, contract,
            PaidOn(stall.Id, today.AddDays(-3), "OR-3"),
            PaidOn(stall.Id, today.AddDays(-4), "OR-3"));
        await context.SaveChangesAsync();

        var rows = await new TransactionFeedRepository(context, new EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(context), new SystemClock()).GetRecentTransactionsAsync(FacilityCode.NPM, today, 50, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Contains("2 days", row.Reference);
        Assert.Contains(today.AddDays(-4).ToString("MMM yyyy"), row.Reference);
    }
}
