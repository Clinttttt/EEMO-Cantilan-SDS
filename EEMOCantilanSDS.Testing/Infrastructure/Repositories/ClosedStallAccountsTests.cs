using EEMOCantilanSDS.Domain.Constants;
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
    public async Task ClosedNpmStall_StatesTheMonthlyEquivalentOfTheTenantsDailyRate()
    {
        // Same defect as the stallholder roster: the register showed the hand-entered Stall.MonthlyRate
        // (₱900 — Cantilan's figure) for a ₱40/day municipality. It must state ₱40 × 30 = ₱1,200 instead,
        // resolved through the very rule the arrears beside it are computed with.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Diego Brando", "Diego Brando", new DateOnly(2026, 6, 1), 3, 900m);
        var rate = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, new DateOnly(2020, 1, 1), Guid.Empty);
        stall.Close(new DateOnly(2026, 6, 10), "Head");

        context.AddRange(facility, stall, contract, rate);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal(1_200m, row.MonthlyRate);
    }

    [Fact]
    public async Task ClosedNpmStall_WithNoTenantRate_KeepsTheOrdinanceFigure()
    {
        // Cantilan's case: no rate rows → ₱30 ordinance → ₱900, exactly what the register showed before.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 6, 1), 3, 900m);
        stall.Close(new DateOnly(2026, 6, 10), "Head");

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, row.MonthlyRate);
        Assert.Equal(900m, row.MonthlyRate);
    }

    [Fact]
    public async Task ClosedNpmStall_CarriesTheTenantsOwnSectionLabel()
    {
        // The register can be filtered and printed one section at a time, so each row must state its section
        // using the LGU's own label — a municipality that calls its vegetable section "Gulayan" must not see
        // the canonical name, and a custom section must state its own name.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        facility.SetSectionLabels("Gulayan", null, null);

        var canonical = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var custom = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: null, customSectionName: "Sari-sari Area");
        var canonicalContract = Contract.Create(canonical.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 6, 1), 3, 900m);
        var customContract = Contract.Create(custom.Id, "Ben Cruz", "Ben Cruz", new DateOnly(2026, 6, 1), 3, 900m);
        canonical.Close(new DateOnly(2026, 6, 10), "Head");
        custom.Close(new DateOnly(2026, 6, 10), "Head");

        context.AddRange(facility, canonical, custom, canonicalContract, customContract);
        await context.SaveChangesAsync();

        var rows = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);

        Assert.Equal("Gulayan", rows.Single(r => r.StallNo == "1").Section);
        Assert.Equal("Sari-sari Area", rows.Single(r => r.StallNo == "2").Section);
    }

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

        // Lapsed, not finished: the term ran out but the space was never handed over, so the office keeps
        // collecting and the account stays in the arrears lists as well as on this register.
        Assert.Equal(InactiveAccountState.Lapsed, row.State);
        Assert.Null(row.ClosedOn);
        Assert.Equal(new DateOnly(2025, 1, 1), row.ExpiryDate);
        Assert.Equal(0m, row.LifetimeCollected);
        // A term of N years owes exactly N × 12 months' rent: one year from 1 Jan 2024 is January to December 2024,
        // twelve months at ₱1,000. The obligation used to bill every calendar month the term OVERLAPPED, so the
        // anniversary month was charged as a thirteenth — every monthly-billed account in the office read one month
        // of rent too high.
        Assert.Equal(12_000m, row.Uncollected);
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
