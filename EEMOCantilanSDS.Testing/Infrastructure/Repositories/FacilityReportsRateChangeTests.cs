using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Per-stall monthly rates are admin-entered and can change mid-contract. A rate change must NOT
/// retroactively re-price months that already have a payment record: those are billed at the rate
/// snapshot stored on the record. Only months without a record yet are billed at the current rate.
/// </summary>
public class FacilityReportsRateChangeTests : RepositoryTestBase
{
    [Fact]
    public async Task RateIncrease_PaidMonthsAtOldRate_ShowNoPhantomBalance()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        // Current (raised) rate is ₱1,000.
        var stall = Stall.Create(facility.Id, "1", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant A", "Tenant A", new DateOnly(2024, 1, 1), 5, 1000m);
        context.AddRange(facility, stall, contract);

        // Jan–Mar 2024 were PAID IN FULL back when the rate was ₱900 (snapshot on the record).
        foreach (var m in new[] { 1, 2, 3 })
        {
            var rec = PaymentRecord.Create(stall.Id, 2024, m, 900m);
            rec.UpdateStatus(PaymentStatus.Paid);
            context.Add(rec);
        }
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Yearly, 2024, null, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        // Paid at ₱900 each → ₱2,700 collected, and those 3 months contribute ₱0 balance (not ₱100 each).
        Assert.Equal(2700m, c.AmountPaid);
        // 9 unrecorded due months at the CURRENT ₱1,000 = ₱9,000 (the only real balance).
        Assert.Equal(9000m, c.Balance);
        Assert.Equal("Partial", c.Status);
    }

    [Fact]
    public async Task UnrecordedMonths_BillAtCurrentRate()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var stall = Stall.Create(facility.Id, "7", 1200m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant B", "Tenant B", new DateOnly(2024, 1, 1), 5, 1200m);
        context.AddRange(facility, stall, contract);
        // Only one paid month recorded (at the current ₱1,200); the rest have no record.
        var jan = PaymentRecord.Create(stall.Id, 2024, 1, 1200m); jan.UpdateStatus(PaymentStatus.Paid);
        context.Add(jan);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NCC, ReportPeriod.Yearly, 2024, null, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(1200m, c.AmountPaid);
        Assert.Equal(13200m, c.Balance);   // 11 unrecorded due months × ₱1,200
    }
}
