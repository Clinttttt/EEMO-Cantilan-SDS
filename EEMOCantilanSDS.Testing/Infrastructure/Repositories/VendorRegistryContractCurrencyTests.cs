using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Vendor Registry (hero counts + table) must list only current vendors: an active stall whose
/// contract term has lapsed is excluded, while closed stalls remain (for their own count).
/// </summary>
public class VendorRegistryContractCurrencyTests : RepositoryTestBase
{
    [Fact]
    public async Task Registry_ExcludesActiveExpiredContracts_ButKeepsCurrentAndClosed()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // Active, contract still current (expires in the future) — included.
        var current = Stall.Create(facility.Id, "1", 2400m, ApplicableFees.BaseRental);
        var currentContract = Contract.Create(current.Id, "Current Payor", "Current Payor", today.AddYears(-1), 3, 2400m);

        // Active, contract lapsed (expired 2 years ago) — excluded.
        var expired = Stall.Create(facility.Id, "2", 2400m, ApplicableFees.BaseRental);
        var expiredContract = Contract.Create(expired.Id, "Expired Payor", "Expired Payor", today.AddYears(-5), 3, 2400m);

        // Closed stall (contract also lapsed) — kept as a closed record.
        var closed = Stall.Create(facility.Id, "3", 2400m, ApplicableFees.BaseRental);
        closed.Close(today.AddDays(-10));
        var closedContract = Contract.Create(closed.Id, "Closed Payor", "Closed Payor", today.AddYears(-5), 3, 2400m);

        context.AddRange(facility, current, currentContract, expired, expiredContract, closed, closedContract);
        await context.SaveChangesAsync();

        var repo = new VendorRepository(context);
        var registry = await repo.GetVendorRegistryAsync(today.Year, today.Month, CancellationToken.None);

        Assert.Equal(2, registry.TotalVendors);            // current + closed (expired-active dropped)
        Assert.Equal(1, registry.ActiveVendors);           // only the current one
        Assert.Equal(1, registry.ClosedVendors);
        Assert.Equal(1, registry.MonthlyBillableVendors);  // only the current active monthly stall
        Assert.Equal(2400m, registry.MonthlyTarget);       // expired stall's rate not counted

        Assert.Contains(registry.Vendors, v => v.StallNo == "1");
        Assert.DoesNotContain(registry.Vendors, v => v.StallNo == "2");
        Assert.Contains(registry.Vendors, v => v.StallNo == "3");
    }

    /// <summary>
    /// A partially-paid monthly stall still owes a balance, so it is NOT "paid this month": it folds
    /// into the Unpaid bucket (keeping Paid + Unpaid == billable an invariant, and matching
    /// TotalOutstanding, which carries the remaining balance) while also being surfaced via PartialCount.
    /// </summary>
    [Fact]
    public async Task Registry_FoldsPartialIntoUnpaid_AndSurfacesPartialCount()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        // Fully paid.
        var paid = Stall.Create(facility.Id, "1", 2400m, ApplicableFees.BaseRental);
        var paidContract = Contract.Create(paid.Id, "Paid Payor", "Paid Payor", today.AddYears(-1), 3, 2400m);
        var paidPay = PaymentRecord.Create(paid.Id, today.Year, today.Month, 2400m);
        paidPay.UpdateStatus(PaymentStatus.Paid);

        // Partially paid — still owes 1,400.
        var partial = Stall.Create(facility.Id, "2", 2400m, ApplicableFees.BaseRental);
        var partialContract = Contract.Create(partial.Id, "Partial Payor", "Partial Payor", today.AddYears(-1), 3, 2400m);
        var partialPay = PaymentRecord.Create(partial.Id, today.Year, today.Month, 2400m);
        partialPay.UpdateStatus(PaymentStatus.Partial, 1000m);

        // Fully unpaid — no payment record at all.
        var unpaid = Stall.Create(facility.Id, "3", 2400m, ApplicableFees.BaseRental);
        var unpaidContract = Contract.Create(unpaid.Id, "Unpaid Payor", "Unpaid Payor", today.AddYears(-1), 3, 2400m);

        context.AddRange(facility, paid, paidContract, paidPay, partial, partialContract, partialPay, unpaid, unpaidContract);
        await context.SaveChangesAsync();

        var repo = new VendorRepository(context);
        var registry = await repo.GetVendorRegistryAsync(today.Year, today.Month, CancellationToken.None);

        Assert.Equal(3, registry.MonthlyBillableVendors);
        Assert.Equal(1, registry.PaidThisMonth);   // only the fully-paid stall
        Assert.Equal(1, registry.PartialCount);     // partial surfaced separately
        Assert.Equal(2, registry.UnpaidCount);      // partial + fully-unpaid both counted as unpaid
        // Invariant: paid + unpaid == billable (partial lives inside unpaid).
        Assert.Equal(registry.MonthlyBillableVendors, registry.PaidThisMonth + registry.UnpaidCount);
        // Outstanding = partial remainder (2,400 − 1,000) + full unpaid (2,400) = 3,800.
        Assert.Equal(3800m, registry.TotalOutstanding);
    }
}
