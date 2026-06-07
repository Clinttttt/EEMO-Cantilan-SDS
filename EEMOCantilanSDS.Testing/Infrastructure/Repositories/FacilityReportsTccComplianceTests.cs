using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Monthly-billed facilities (TCC etc.) must aggregate ALL payment records in the selected period.
/// Regression: the Yearly view previously reflected only the most recent month's record.
/// </summary>
public class FacilityReportsTccComplianceTests : RepositoryTestBase
{
    [Fact]
    public async Task YearlyCompliance_AggregatesEveryMonthlyRecord_NotJustLatest()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant A", "Tenant A", new DateOnly(2024, 1, 1), 5, 1000m);

        // Three months in 2024: Jan paid, Feb paid, Mar partial ₱400 (of ₱1000).
        var jan = PaymentRecord.Create(stall.Id, 2024, 1, 1000m); jan.UpdateStatus(PaymentStatus.Paid);
        var feb = PaymentRecord.Create(stall.Id, 2024, 2, 1000m); feb.UpdateStatus(PaymentStatus.Paid);
        var mar = PaymentRecord.Create(stall.Id, 2024, 3, 1000m); mar.UpdateStatus(PaymentStatus.Partial, 400m);

        context.AddRange(facility, stall, contract, jan, feb, mar);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Yearly, 2024, null, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        // Full 2024 obligation = 12 × ₱1000 = ₱12,000; paid = 1000 + 1000 + 400 = ₱2400;
        // balance = ₱9,600 (Mar's ₱600 shortfall plus the 9 months that were never recorded but
        // are still owed under the contract).
        Assert.Equal(2400m, row.AmountPaid);
        Assert.Equal(9600m, row.Balance);
        Assert.Equal("Partial", row.Status);
        // Outstanding KPI must match the aggregated balance.
        Assert.Equal(9600m, report.PendingPaymentAmount);
    }

    [Fact]
    public async Task MonthlyCompliance_SingleMonth_Unchanged()
    {
        // Regression guard: monthly view (one record in range) is identical to before.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant A", "Tenant A", new DateOnly(2024, 1, 1), 5, 1000m);
        var jun = PaymentRecord.Create(stall.Id, 2024, 6, 1000m); jun.UpdateStatus(PaymentStatus.Partial, 250m);

        context.AddRange(facility, stall, contract, jun);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2024, 6, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(250m, row.AmountPaid);
        Assert.Equal(750m, row.Balance);
        Assert.Equal("Partial", row.Status);
    }
    [Fact]
    public async Task TrendExpected_IncludesUnpaidStallWithoutRecord()
    {
        // Bar height must scale against the FULL obligation: an unpaid stall (no PaymentRecord)
        // still raises the expected baseline, so a fully-collected bar is only proportional.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var paidA = Stall.Create(facility.Id, "1", 1000m, ApplicableFees.BaseRental);
        var paidB = Stall.Create(facility.Id, "2", 1000m, ApplicableFees.BaseRental);
        var unpaid = Stall.Create(facility.Id, "3", 1000m, ApplicableFees.BaseRental); // no payment record
        var start = new DateOnly(2024, 1, 1);
        var cA = Contract.Create(paidA.Id, "A", "A", start, 5, 1000m);
        var cB = Contract.Create(paidB.Id, "B", "B", start, 5, 1000m);
        var cC = Contract.Create(unpaid.Id, "C", "C", start, 5, 1000m);
        var payA = PaymentRecord.Create(paidA.Id, 2024, 6, 1000m); payA.UpdateStatus(PaymentStatus.Paid);
        var payB = PaymentRecord.Create(paidB.Id, 2024, 6, 1000m); payB.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, paidA, paidB, unpaid, cA, cB, cC, payA, payB);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2024, 6, null, CancellationToken.None);

        var june = report.RevenueTrend.Single(p => p.PeriodLabel == "Jun 2024");
        Assert.Equal(2000m, june.Revenue);          // 2 stalls paid
        Assert.Equal(3000m, june.ExpectedRevenue);  // 3 stalls owe — includes the unpaid no-record stall
    }



    [Fact]
    public async Task CollectionRate_CountsUnpaidStallWithoutRecord_InDenominator()
    {
        // Regression: rate must not read 100% while a payor still owes. 2 of 3 stalls paid,
        // the 3rd unpaid with no record → 2000 / 3000 = 67%, not 100%.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var paidA = Stall.Create(facility.Id, "1", 1000m, ApplicableFees.BaseRental);
        var paidB = Stall.Create(facility.Id, "2", 1000m, ApplicableFees.BaseRental);
        var unpaid = Stall.Create(facility.Id, "3", 1000m, ApplicableFees.BaseRental);
        var start = new DateOnly(2024, 1, 1);
        var cA = Contract.Create(paidA.Id, "A", "A", start, 5, 1000m);
        var cB = Contract.Create(paidB.Id, "B", "B", start, 5, 1000m);
        var cC = Contract.Create(unpaid.Id, "C", "C", start, 5, 1000m);
        var payA = PaymentRecord.Create(paidA.Id, 2024, 6, 1000m); payA.UpdateStatus(PaymentStatus.Paid);
        var payB = PaymentRecord.Create(paidB.Id, 2024, 6, 1000m); payB.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, paidA, paidB, unpaid, cA, cB, cC, payA, payB);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2024, 6, null, CancellationToken.None);

        Assert.Equal(67m, Math.Round(report.CollectionRate));
        Assert.Equal(1000m, report.PendingPaymentAmount); // the unpaid stall's balance
    }

    [Fact]
    public async Task YearlyMissedMonths_DoesNotCountFutureMonths_InCurrentYear()
    {
        // P4: a yearly view of the current year must not flag months that haven't started yet.
        var context = NewContext();
        var today = PhilippineTime.Today;

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        // Contract effective Jan 1 of the current year, no payments at all.
        var contract = Contract.Create(stall.Id, "No Pay", "No Pay", new DateOnly(today.Year, 1, 1), 5, 1000m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Yearly, today.Year, null, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        // Only months Jan..currentMonth are due → exactly today.Month missed, not 12.
        Assert.Equal(today.Month, row.MissedMonths);
    }

    [Fact]
    public async Task YearlyCompliance_UnpaidStall_BalanceReconcilesWithMissedMonthsAndRate()
    {
        // Option B: a stall under contract all of a past year, with no payments, owes every month.
        // Balance, Outstanding, Collection Rate and Missed-months must all reconcile.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "No Pay", "No Pay", new DateOnly(2024, 1, 1), 5, 1000m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Yearly, 2024, null, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal("Unpaid", row.Status);
        Assert.Equal(0m, row.AmountPaid);
        Assert.Equal(12, row.MissedMonths);                 // 2024 fully past → 12 due months
        Assert.Equal(12_000m, row.Balance);                 // 12 × ₱1,000
        Assert.Equal(row.MissedMonths * 1000m, row.Balance); // balance reconciles with missed months
        Assert.Equal(12_000m, report.PendingPaymentAmount);  // Outstanding KPI matches
        Assert.Equal(0m, report.CollectionRate);             // nothing collected of the full obligation
    }
}
