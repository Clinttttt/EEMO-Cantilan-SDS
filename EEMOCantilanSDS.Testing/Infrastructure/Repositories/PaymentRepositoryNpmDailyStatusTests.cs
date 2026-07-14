using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The NPM operational table is driven by daily-collection status (paid today / days paid /
/// last paid), not by monthly payment records — because NPM collects ₱30/day.
/// </summary>
public class PaymentRepositoryNpmDailyStatusTests : RepositoryTestBase
{
    [Fact]
    public async Task DailyStatus_ReportsPaidToday_DaysPaid_AndLastPaid()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 1, 1), 3, 900m);

        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Three paid days this month, including today.
        var days = new[] { monthStart, monthStart.AddDays(1), today }.Distinct().ToArray();
        var collections = days.Select(d =>
        {
            var dc = DailyCollection.Create(stall.Id, d);
            dc.MarkPaid($"OR-{d:yyyyMMdd}", Guid.NewGuid());
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, contract);
        context.AddRange(collections);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        var row = Assert.Single(status);
        Assert.Equal(stall.Id, row.StallId);
        Assert.True(row.PaidToday);
        Assert.Equal(days.Length, row.DaysPaidThisMonth);
        Assert.Equal(today, row.LastPaidDate);
    }

    [Fact]
    public async Task DailyStatus_IncludesCurrentMonthUtilityStatus()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Fisher Joe", "Fisher Joe", new DateOnly(2026, 1, 1), 3, 900m);

        var today = PhilippineTime.Today;
        var dc = DailyCollection.Create(stall.Id, today);
        dc.MarkPaid("OR-1", Guid.NewGuid());

        // Utility bill this month: electricity fully paid, water still unpaid → overall Partial.
        var bill = UtilityBill.Create(stall.Id, today.Year, today.Month, 0m, 100m, 10m, 0m, 50m, 20m);
        bill.RecordPayment("E-1", null, null, PaymentStatus.Paid, null, PaymentStatus.Unpaid, null);

        context.AddRange(facility, stall, contract);
        context.Add(dc);
        context.Add(bill);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var row = Assert.Single(await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None));
        Assert.Equal(PaymentStatus.Partial, row.UtilityStatus);
    }

    [Fact]
    public async Task DailyStatus_StallWithNoCollections_IsAbsent()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var today = PhilippineTime.Today;
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        // No paid daily collections → stall does not appear; the page treats it as "Not yet / 0 days".
        Assert.Empty(status);
    }

    [Fact]
    public async Task DailyStatus_NotPaidToday_WhenLatestCollectionIsEarlier()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Lorna Buenades", "Lorna Buenades", new DateOnly(2026, 1, 1), 3, 900m);

        var today = PhilippineTime.Today;
        var earlier = new DateOnly(today.Year, today.Month, 1);
        // Ensure "earlier" is strictly before today; if today is the 1st, skip the assertion premise.
        if (earlier == today) earlier = today; // first-of-month edge: only one day available

        var dc = DailyCollection.Create(stall.Id, earlier);
        dc.MarkPaid("OR-EARLIER", Guid.NewGuid());

        context.AddRange(facility, stall, contract, dc);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        var row = Assert.Single(status);
        Assert.Equal(earlier == today, row.PaidToday); // false unless today is the 1st
        Assert.Equal(earlier, row.LastPaidDate);
        Assert.Equal(1, row.DaysPaidThisMonth);
    }

    [Fact]
    public async Task DailyStatus_MarkedAbsentToday_ReportsAbsentToday_NotPaid()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 1, 1), 3, 900m);

        var today = PhilippineTime.Today;
        var absent = DailyCollection.Create(stall.Id, today);
        absent.MarkAbsent();

        context.AddRange(facility, stall, contract, absent);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        var row = Assert.Single(status);
        Assert.True(row.AbsentToday);
        Assert.False(row.PaidToday);
        Assert.Equal(0, row.DaysPaidThisMonth);
    }

    [Fact]
    public async Task UnreceiptedForYear_GroupsBlankOrDailyPerStallPerMonth_ExcludesReceiptedAndOtherYears()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", new DateOnly(2023, 6, 7), 5, 900m);
        context.AddRange(facility, stall, contract);

        // March 2026: 3 paid days, blank OR (₱30 × 3 = ₱90).
        foreach (var d in new[] { new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3) })
        {
            var dc = DailyCollection.Create(stall.Id, d);
            dc.MarkPaid("", null);   // paid, no receipt number
            context.Add(dc);
        }
        // May 2026: 2 paid days, blank OR (₱30 × 2 = ₱60).
        foreach (var d in new[] { new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 11) })
        {
            var dc = DailyCollection.Create(stall.Id, d);
            dc.MarkPaid("", null);
            context.Add(dc);
        }
        // A RECEIPTED April day (has OR) — excluded.
        var receipted = DailyCollection.Create(stall.Id, new DateOnly(2026, 4, 1));
        receipted.MarkPaid("OR-APR-1", null);
        context.Add(receipted);
        // A blank-OR day in ANOTHER year — excluded.
        var otherYear = DailyCollection.Create(stall.Id, new DateOnly(2025, 3, 1));
        otherYear.MarkPaid("", null);
        context.Add(otherYear);

        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var rows = await repo.GetUnreceiptedCashPaymentsForYearAsync(2026, CancellationToken.None);

        // One row per (stall, month) — March and May only.
        Assert.Equal(2, rows.Count);

        var mar = Assert.Single(rows, r => r.Month == 3);
        Assert.True(mar.IsDaily);
        Assert.Equal(2026, mar.Year);
        Assert.Equal(3, mar.Count);
        Assert.Equal(90m, mar.Amount);
        Assert.Equal("Ramil C. Orjeles", mar.Occupant);

        var may = Assert.Single(rows, r => r.Month == 5);
        Assert.Equal(2, may.Count);
        Assert.Equal(60m, may.Amount);
    }
}
