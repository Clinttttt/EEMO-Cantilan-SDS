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
}
