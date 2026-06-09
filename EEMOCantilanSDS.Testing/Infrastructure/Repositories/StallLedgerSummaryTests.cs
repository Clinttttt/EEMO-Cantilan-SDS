using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The stall profile's 12-month summary must reconcile with the reports: NPM totals fold daily
/// collections against a contract-aware ₱30/day obligation; pre-contract months are excluded; other
/// facilities use the monthly rent. Counts only fully-covered months as "paid".
/// </summary>
public class StallLedgerSummaryTests : RepositoryTestBase
{
    [Fact]
    public async Task Summary_Npm_FoldsDailyCollectionsAndExcludesPreContractMonths()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        // Contract effective two whole months ago → exactly 3 collectable months in the window.
        var contract = Contract.Create(stall.Id, "Lorna Buenades", "Lorna Buenades", monthStart.AddMonths(-2), 3, 900m);
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");

        // Two paid days this month only (₱60), mirroring the reported scenario.
        var days = new[] { monthStart, monthStart.AddDays(1) }.Where(d => d <= today).ToArray();
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
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // Collected = exactly the daily fees paid (₱30 × paid days) — not MonthlyRate × count.
        Assert.Equal(days.Length * FeeRates.NpmDailyFee, summary.TotalCollected);
        // 3 collectable months, none fully covered (current is partial, prior two unpaid).
        Assert.Equal(0, summary.MonthsPaid);
        Assert.Equal(3, summary.MonthsUnpaid);
        Assert.True(summary.TotalOutstanding > 0m);
    }

    [Fact]
    public async Task Summary_NonNpm_PaidMonth_HasNoOutstanding()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", monthStart, 3, 1000m); // starts this month → 1 collectable month
        var payment = PaymentRecord.Create(stall.Id, today.Year, today.Month, 1000m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(1, summary.MonthsPaid);
        Assert.Equal(0, summary.MonthsUnpaid);
        Assert.Equal(1000m, summary.TotalCollected);
        Assert.Equal(0m, summary.TotalOutstanding);
    }

    [Fact]
    public async Task Summary_NonNpm_UnpaidMonth_OwesFullRent()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "102", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", monthStart, 3, 1000m); // 1 collectable month, no payment

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(0, summary.MonthsPaid);
        Assert.Equal(1, summary.MonthsUnpaid);
        Assert.Equal(0m, summary.TotalCollected);
        Assert.Equal(1000m, summary.TotalOutstanding);
    }
}
