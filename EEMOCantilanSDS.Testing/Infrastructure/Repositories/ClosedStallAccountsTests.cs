using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Closed/Inactive Accounts register: closed (frozen) AND expired (contract lapsed) stalls, each
/// with lifetime collected (all money ever received) and uncollected arrears accrued up to the end
/// point (close date / contract expiry), excused/absent-aware. Vacant and active-in-term stalls are
/// excluded.
/// </summary>
public class ClosedStallAccountsTests : RepositoryTestBase
{
    [Fact]
    public async Task ClosedMonthlyStall_ReportsLifetimeCollected_AndArrearsUpToCloseMonth()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Pedro Gallardo", "Pedro Gallardo", new DateOnly(2026, 1, 1), 5, 1000m);

        // Jan & Feb paid; Mar & Apr never paid. Closed mid-April → register window is Jan..Apr.
        var jan = PaymentRecord.Create(stall.Id, 2026, 1, 1000m); jan.UpdateStatus(PaymentStatus.Paid);
        var feb = PaymentRecord.Create(stall.Id, 2026, 2, 1000m); feb.UpdateStatus(PaymentStatus.Paid);
        stall.Close(new DateOnly(2026, 4, 15), "Head");

        context.AddRange(facility, stall, contract, jan, feb);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var row = Assert.Single(await repo.GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal(InactiveAccountState.Closed, row.State);
        Assert.Equal(new DateOnly(2026, 4, 15), row.ClosedOn);
        Assert.Equal("Head", row.ClosedBy);
        Assert.Equal(2000m, row.LifetimeCollected);   // Jan + Feb paid
        Assert.Equal(2000m, row.Uncollected);          // Mar + Apr full rent owed
    }

    [Fact]
    public async Task ClosedNpmStall_CountsPaidDailiesAsCollected_AndUnpaidNonAbsentDaysAsArrears()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "F-5", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Lorna B.", "Lorna B.", new DateOnly(2026, 6, 1), 5, 900m);

        // Jun 1-3 paid; Jun 8-9 absent (excused); closed Jun 10 → window Jun 1..Jun 10.
        var paid = new[] { 1, 2, 3 }.Select(d => { var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, d)); dc.MarkPaid($"OR-{d}", Guid.NewGuid()); return dc; }).ToArray();
        var absent = new[] { 8, 9 }.Select(d => { var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, d)); dc.MarkAbsent("Head"); return dc; }).ToArray();
        stall.Close(new DateOnly(2026, 6, 10), "Head");

        context.AddRange(facility, stall, contract);
        context.AddRange(paid);
        context.AddRange(absent);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var row = Assert.Single(await repo.GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal(InactiveAccountState.Closed, row.State);
        Assert.Equal(3 * FeeRates.NpmDailyFee, row.LifetimeCollected);    // 3 paid days
        // Unpaid, non-absent contract days in [Jun1..Jun10]: 4,5,6,7,10 = 5 days.
        Assert.Equal(5 * FeeRates.NpmDailyFee, row.Uncollected);
    }

    [Fact]
    public async Task ExpiredStall_AppearsAsExpired_WithArrearsUpToContractExpiry()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var stall = Stall.Create(facility.Id, "Ext-2", 1000m, ApplicableFees.BaseRental);
        // 2024-01-01 + 1yr → expired 2025-01-01 (today is 2026). Active (never manually closed).
        var contract = Contract.Create(stall.Id, "Lita Soriano", "Lita Soriano", new DateOnly(2024, 1, 1), 1, 1000m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var row = Assert.Single(await repo.GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal(InactiveAccountState.Expired, row.State);
        Assert.Null(row.ClosedOn);
        Assert.Equal(new DateOnly(2025, 1, 1), row.ExpiryDate);
        Assert.Equal(0m, row.LifetimeCollected);
        // Months overlapping [2024-01 .. 2025-01] = 13 months × ₱1000 (same month-overlap rule the reports bill on).
        Assert.Equal(13_000m, row.Uncollected);
    }

    [Fact]
    public async Task ActiveInTermStall_AndVacantStall_AreExcluded()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");

        var active = Stall.Create(facility.Id, "201", 1000m, ApplicableFees.BaseRental);
        active.Contracts.Add(Contract.Create(active.Id, "Active Tenant", "Active Tenant", new DateOnly(2026, 1, 1), 5, 1000m)); // not expired

        var vacant = Stall.Create(facility.Id, "202", 1000m, ApplicableFees.BaseRental); // no contract

        context.AddRange(facility, active, vacant);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var rows = await repo.GetClosedStallAccountsAsync(CancellationToken.None);

        Assert.Empty(rows);
    }
}
