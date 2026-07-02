using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
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
}
