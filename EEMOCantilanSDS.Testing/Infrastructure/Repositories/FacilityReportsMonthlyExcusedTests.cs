using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Monthly "Excused" recognition (TCC/NCC/BBQ/ICE): an admin-approved excused month owes ₱0, drops
/// out of the rent obligation, reads as the distinct "Excused" status, and never surfaces as
/// delinquent — even when an Unpaid PaymentRecord exists for that month.
/// </summary>
public class FacilityReportsMonthlyExcusedTests : RepositoryTestBase
{
    private static (Facility f, Stall s, Contract c) Tcc()
    {
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Marlex Dumagay", "Marlex Dumagay", new DateOnly(2026, 1, 1), 3, 2400m);
        return (facility, stall, contract);
    }

    [Fact]
    public async Task ExcusedMonth_IsNotOwed_ReadsExcused_AndNotDelinquent()
    {
        var context = NewContext();
        var (facility, stall, contract) = Tcc();
        context.AddRange(facility, stall, contract);

        // An Unpaid record AND an excused exception for June 2026.
        var unpaid = PaymentRecord.Create(stall.Id, 2026, 6, 2400m, "seed");
        unpaid.UpdateStatus(PaymentStatus.Unpaid, 0m, null, "seed", null);
        context.Add(unpaid);
        context.Add(StallMonthlyException.Create(stall.Id, 2026, 6, MonthlyExceptionReason.ApprovedByEemo));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal("Excused", c.Status);
        Assert.Equal(0m, c.Balance);
        Assert.Equal(0m, c.ExpectedBill);   // June rent is excused → ₱0 owed

        // Window ending July 2026 includes June; the excused month must be filtered out of delinquency.
        var delinquent = await repo.GetDelinquentStallsAsync(FacilityCode.TCC, 2026, 7, CancellationToken.None);
        Assert.DoesNotContain(delinquent, d => d.StallNo == "1");
    }

    [Fact]
    public async Task WithoutException_UnpaidMonth_IsOwed_AndDelinquent()
    {
        var context = NewContext();
        var (facility, stall, contract) = Tcc();
        context.AddRange(facility, stall, contract);

        var unpaid = PaymentRecord.Create(stall.Id, 2026, 6, 2400m, "seed");
        unpaid.UpdateStatus(PaymentStatus.Unpaid, 0m, null, "seed", null);
        context.Add(unpaid);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal("Unpaid", c.Status);
        Assert.Equal(2400m, c.Balance);

        var delinquent = await repo.GetDelinquentStallsAsync(FacilityCode.TCC, 2026, 7, CancellationToken.None);
        Assert.Contains(delinquent, d => d.StallNo == "1");
    }

    [Fact]
    public async Task Yearly_ExcusedMonths_ReduceObligation_ByExactlyThoseMonths()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant A", "Tenant A", new DateOnly(2024, 1, 1), 5, 1000m);

        // 2024 (entirely past): Jan paid; Apr/May/Jun admin-excused; the other 8 months never recorded.
        var jan = PaymentRecord.Create(stall.Id, 2024, 1, 1000m); jan.UpdateStatus(PaymentStatus.Paid);
        context.AddRange(facility, stall, contract, jan);
        foreach (var m in new[] { 4, 5, 6 })
            context.Add(StallMonthlyException.Create(stall.Id, 2024, m, MonthlyExceptionReason.ApprovedByEemo));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Yearly, 2024, null, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        // Raw obligation 12 × ₱1000 = ₱12,000; 3 excused months removed → ₱9,000 owed; paid ₱1,000.
        Assert.Equal(1000m, row.AmountPaid);
        Assert.Equal(8000m, row.Balance);            // 9,000 − 1,000
        Assert.Equal("Partial", row.Status);
        Assert.Equal(8000m, report.PendingPaymentAmount);   // Outstanding excludes excused months
    }
}
