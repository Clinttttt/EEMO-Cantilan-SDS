using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Payment history must reflect NPM's daily collections — a stall paying ₱30/day is not "Unpaid"
/// for the month. Non-NPM facilities keep the monthly-record ledger unchanged.
/// </summary>
public class PaymentHistoryNpmTests : RepositoryTestBase
{
    [Fact]
    public async Task History_Npm_FoldsDailyCollectionsIntoCurrentMonth()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Contract effective well before this month so the whole month is collectable.
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", monthStart.AddMonths(-2), 3, 900m);
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");

        // Three paid days this month (₱90 total).
        var days = new[] { monthStart, monthStart.AddDays(1), monthStart.AddDays(2) }
            .Where(d => d <= today).ToArray();
        var collections = days.Select(d =>
        {
            var dc = DailyCollection.Create(stall.Id, d);
            dc.MarkPaid($"OR-{d:yyyyMMdd}", collector.Id);
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, contract, collector);
        context.AddRange(collections);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        var currentKey = $"{today.Year:0000}-{today.Month:00}";
        var row = history.Single(h => h.Period == currentKey);

        Assert.Equal(days.Length * FeeRates.NpmDailyFee, row.AmountPaid);  // ₱30 × days
        Assert.Equal(PaymentStatus.Partial, row.Status);                    // not a full month
        Assert.True(row.BalanceDue > 0m);
        Assert.Equal("Juan Dela Cruz", row.CollectorName);
        Assert.False(string.IsNullOrEmpty(row.ORNumber));
    }

    [Fact]
    public async Task History_Npm_DoesNotBillMonthsBeforeContractStart()
    {
        // Regression: a contract starting this month must not show prior months as owed.
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", monthStart, 3, 900m); // starts this month
        var dc = DailyCollection.Create(stall.Id, today);
        dc.MarkPaid("OR-1", Guid.NewGuid());

        context.AddRange(facility, stall, contract, dc);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        // Only the current month (which has a collection) is emitted; no prior-month rows.
        var row = Assert.Single(history);
        Assert.Equal($"{today.Year:0000}-{today.Month:00}", row.Period);
    }

    [Fact]
    public async Task History_Npm_MonthWithNoCollections_IsOmitted()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Lorna", "Lorna", monthStart.AddMonths(-2), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        // No daily collections anywhere → no rows emitted; the modal renders months as
        // Unpaid / before-contract on its own. (Emitting zero rows would break that.)
        Assert.Empty(history);
    }

    [Fact]
    public async Task History_NonNpm_UsesMonthlyRecordsUnchanged()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", new DateOnly(today.Year, 1, 1), 3, 1000m);
        var payment = PaymentRecord.Create(stall.Id, today.Year, today.Month, 1000m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        var row = history.Single(h => h.Period == $"{today.Year:0000}-{today.Month:00}");
        Assert.Equal(PaymentStatus.Paid, row.Status);
        Assert.Equal(1000m, row.AmountPaid);
    }

    [Fact]
    public async Task History_Npm_MonthlyPartialRecord_IsIgnoredInFavorOfDailyCollections()
    {
        // Reported bug: a flat monthly PaymentRecord (Partial, ₱900 base, ₱500 partial) was
        // overriding the daily collections, so the 12-month modal showed ₱500 collected / ₱400
        // balance instead of the daily reality (₱30/day). NPM money must always be daily-truth.
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", monthStart.AddMonths(-2), 3, 900m);
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");

        // Two paid daily collections this month = ₱60.
        var d1 = DailyCollection.Create(stall.Id, monthStart); d1.MarkPaid("OR-D1", collector.Id);
        var d2 = DailyCollection.Create(stall.Id, monthStart.AddDays(1)); d2.MarkPaid("OR-D2", collector.Id);

        // The anomalous monthly partial record that used to win.
        var monthly = PaymentRecord.Create(stall.Id, today.Year, today.Month, 900m);
        monthly.UpdateStatus(PaymentStatus.Partial, 500m);
        monthly.SetOrNumber("MONTHLY-OR-500");

        context.AddRange(facility, stall, contract, collector);
        context.AddRange(d1, d2, monthly);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        var row = history.Single(h => h.Period == $"{today.Year:0000}-{today.Month:00}");
        Assert.Equal(2 * FeeRates.NpmDailyFee, row.AmountPaid);                  // ₱60 daily-truth, NOT ₱500
        Assert.Equal(daysInMonth * FeeRates.NpmDailyFee, row.TotalBill);         // full-month ₱30/day obligation
        Assert.Equal(daysInMonth * FeeRates.NpmDailyFee - 60m, row.BalanceDue);  // NOT 900 − 500 = 400
        Assert.Equal(PaymentStatus.Partial, row.Status);
        Assert.NotEqual("MONTHLY-OR-500", row.ORNumber);                         // OR comes from the daily collection
    }

    [Fact]
    public async Task History_Npm_CountsDaysPaidInAdvanceWithinCurrentMonth()
    {
        // The modal must mirror the daily calendar, which counts every paid day of the month even
        // if the date hasn't arrived yet. The window now runs to month-end, not today.
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", monthStart.AddMonths(-1), 3, 900m);
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");

        // A collection dated the LAST day of the month (>= today): an advance payment.
        var dc = DailyCollection.Create(stall.Id, monthEnd); dc.MarkPaid("OR-LAST", collector.Id);

        context.AddRange(facility, stall, contract, collector, dc);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        var row = history.Single(h => h.Period == $"{today.Year:0000}-{today.Month:00}");
        Assert.Equal(FeeRates.NpmDailyFee, row.AmountPaid);   // the advance/last-day collection is counted
    }

    [Fact]
    public async Task History_Npm_AbsentDays_ReduceMonthlyBill()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", monthStart.AddMonths(-2), 3, 900m);

        // Three excused days + one paid day this month.
        var a1 = DailyCollection.Create(stall.Id, monthStart); a1.MarkAbsent();
        var a2 = DailyCollection.Create(stall.Id, monthStart.AddDays(1)); a2.MarkAbsent();
        var a3 = DailyCollection.Create(stall.Id, monthStart.AddDays(2)); a3.MarkAbsent();
        var p1 = DailyCollection.Create(stall.Id, monthStart.AddDays(3)); p1.MarkPaid("OR-P1", Guid.NewGuid());

        context.AddRange(facility, stall, contract, a1, a2, a3, p1);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        var row = history.Single(h => h.Period == $"{today.Year:0000}-{today.Month:00}");
        Assert.Equal((daysInMonth - 3) * FeeRates.NpmDailyFee, row.TotalBill);  // 3 excused days drop out of the bill
        Assert.Equal(FeeRates.NpmDailyFee, row.AmountPaid);                      // 1 paid day
        Assert.False(row.IsExcused);
    }

    [Fact]
    public async Task History_Npm_FullyAbsentMonth_EmitsExcusedRow_NotUnpaid()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Effective only the last day → exactly one collectable day this month, and it is excused.
        var contract = Contract.Create(stall.Id, "Ben Cruz", "Ben Cruz", monthEnd, 3, 900m);
        var absent = DailyCollection.Create(stall.Id, monthEnd); absent.MarkAbsent();

        context.AddRange(facility, stall, contract, absent);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var history = await repo.GetPaymentHistoryAsync(stall.Id, CancellationToken.None);

        var row = history.Single(h => h.Period == $"{today.Year:0000}-{today.Month:00}");
        Assert.True(row.IsExcused);          // shown as "Absent", not Unpaid
        Assert.Equal(0m, row.TotalBill);
        Assert.Equal(0m, row.AmountPaid);
        Assert.Equal(0m, row.BalanceDue);
    }

    [Fact]
    public async Task LedgerSummary_Npm_FullyAbsentMonth_OwesNothing_NotUnpaid()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ben Cruz", "Ben Cruz", monthEnd, 3, 900m);
        var absent = DailyCollection.Create(stall.Id, monthEnd); absent.MarkAbsent();

        context.AddRange(facility, stall, contract, absent);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(0m, summary.TotalOutstanding);  // fully excused → nothing owed
        Assert.Equal(0, summary.MonthsUnpaid);        // and not counted as an unpaid month
    }
}
