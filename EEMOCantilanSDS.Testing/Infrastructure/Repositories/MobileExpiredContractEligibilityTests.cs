using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Mobile collection/report eligibility must use contract-period coverage, not <c>Contract.IsActive</c>
/// alone. <c>IsActive</c> is a manual flag that does not reflect expiry, so an active-but-lapsed contract
/// must NOT appear as a current collection target or inflate payee/outstanding summaries. Selected month
/// throughout is July 2026; contracts expiring on/ before June 2026 are lapsed for that month.
/// </summary>
public class MobileExpiredContractEligibilityTests : RepositoryTestBase
{
    // Expiry = EffectivityDate.AddYears(DurationYears).
    private static Contract ContractExpiring(Guid stallId, DateOnly effectivity, int years, decimal rate = 900m) =>
        Contract.Create(stallId, "Payor", "Payor", effectivity, years, rate);

    // ── NPM daily collection ──

    [Fact]
    public async Task NpmMobileCollection_ExcludesContractsExpiredBeforeSelectedMonth()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // 2023-06-06 + 3y → expires 2026-06-06, before July 2026.
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2023, 6, 6), 3));
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var result = await repo.GetMobileNpmCollectionAsync(2026, 7, new DateOnly(2026, 7, 15), CancellationToken.None);

        Assert.Empty(result.Stalls);
        Assert.Equal(0, result.TotalStalls);
    }

    [Fact]
    public async Task NpmMobileCollection_IncludesContractThatOverlapsSelectedMonth()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-2", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // 2024-07-01 + 3y → expires 2027-07-01, covers July 2026.
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2024, 7, 1), 3));
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var result = await repo.GetMobileNpmCollectionAsync(2026, 7, new DateOnly(2026, 7, 15), CancellationToken.None);

        Assert.Single(result.Stalls);
    }

    [Fact]
    public async Task NpmMobileCollection_StopsCollectableDaysAtContractExpiry()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Started well before the month, expires mid-month on 2026-07-10.
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2023, 7, 10), 3));
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        // Collection date at month end → without capping this would count all 31 days.
        var result = await repo.GetMobileNpmCollectionAsync(2026, 7, new DateOnly(2026, 7, 31), CancellationToken.None);

        var row = Assert.Single(result.Stalls);
        Assert.Equal(10, row.CollectableDays); // Jul 1–10 only
    }

    // ── Monthly rental collection ──

    [Fact]
    public async Task MonthlyMobileCollection_ExcludesExpiredContractBeforeBillingMonth()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 2400m, ApplicableFees.BaseRental);
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2023, 6, 6), 3, 2400m));
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var result = await repo.GetMobileMonthlyCollectionAsync(FacilityCode.TCC, 2026, 7, new DateOnly(2026, 7, 15), CancellationToken.None);

        Assert.Empty(result.Stalls);
        Assert.Equal(0m, result.OutstandingAmount);
        Assert.Equal(0, result.UnpaidCount);
    }

    [Fact]
    public async Task MonthlyMobileCollection_IncludesContractOverlappingBillingMonth()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "102", 2400m, ApplicableFees.BaseRental);
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2024, 7, 1), 3, 2400m));
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var result = await repo.GetMobileMonthlyCollectionAsync(FacilityCode.TCC, 2026, 7, new DateOnly(2026, 7, 15), CancellationToken.None);

        var row = Assert.Single(result.Stalls);
        Assert.Equal(2400m, result.OutstandingAmount); // full month rent owed
        Assert.Equal(1, result.UnpaidCount);
    }

    // ── Collector report payee summaries ──

    [Fact]
    public async Task CollectorReport_ExcludesExpiredContractsFromPayeeSummaries()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var expired = Stall.Create(facility.Id, "201", 2400m, ApplicableFees.BaseRental);
        var valid = Stall.Create(facility.Id, "202", 2400m, ApplicableFees.BaseRental);
        context.AddRange(
            facility, expired, valid,
            ContractExpiring(expired.Id, new DateOnly(2023, 6, 6), 3, 2400m),   // expires 2026-06-06
            ContractExpiring(valid.Id, new DateOnly(2024, 7, 1), 3, 2400m));     // covers July 2026
        await context.SaveChangesAsync();

        var repo = new CollectorRepository(context);
        var report = await repo.GetCollectorReportAsync(
            Guid.NewGuid(), new[] { FacilityCode.TCC }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);

        // Only the valid stall is a payee; the expired stall must not inflate unpaid / outstanding.
        var payee = Assert.Single(report.Payees);
        Assert.Equal("202", payee.StallNo);
        Assert.Equal(1, report.Totals.PayeeCount);
        Assert.Equal(1, report.Totals.UnpaidCount);
        Assert.Equal(2400m, report.Totals.OutstandingAmount); // one stall, not two
    }

    [Fact]
    public async Task CollectorReport_MonthlyException_CountsAsExcused_NotUnpaid()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "301", 2400m, ApplicableFees.BaseRental);
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2024, 7, 1), 3, 2400m));
        // Excused for July 2026 → owes ₱0, must not read as unpaid.
        context.Add(Domain.Entities.Payments.StallMonthlyException.Create(stall.Id, 2026, 7));
        await context.SaveChangesAsync();

        var repo = new CollectorRepository(context);
        var report = await repo.GetCollectorReportAsync(
            Guid.NewGuid(), new[] { FacilityCode.TCC }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);

        Assert.Empty(report.Payees);                       // excused → not a payee
        Assert.Equal(0, report.Totals.UnpaidCount);        // never counted as unpaid
        Assert.Equal(0m, report.Totals.OutstandingAmount); // ₱0 owed
        Assert.Equal(1, report.Totals.AbsentExcusedCount); // surfaced separately
        var tcc = Assert.Single(report.Facilities);
        Assert.Equal(1, tcc.AbsentExcusedCount);
        var ex = Assert.Single(report.AbsentExcused);
        Assert.Equal("Monthly exception", ex.Source);
        Assert.Equal("301", ex.StallNo);
    }

    [Fact]
    public async Task CollectorReport_NpmAbsentDays_CountAsExcused()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-9", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2024, 7, 1), 3));

        // Two absent (excused) days in July 2026.
        foreach (var day in new[] { 5, 6 })
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 7, day));
            dc.MarkAbsent("Head");
            context.Add(dc);
        }
        await context.SaveChangesAsync();

        var repo = new CollectorRepository(context);
        var report = await repo.GetCollectorReportAsync(
            Guid.NewGuid(), new[] { FacilityCode.NPM }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);

        Assert.Equal(2, report.Totals.AbsentExcusedCount);
        var npm = Assert.Single(report.Facilities);
        Assert.Equal(2, npm.AbsentExcusedCount);
        Assert.Equal(2, report.AbsentExcused.Count);
        Assert.All(report.AbsentExcused, a => Assert.Equal("NPM daily absence", a.Source));
    }

    [Fact]
    public async Task CollectorReport_ExposesTransactionRows_ForDetailView()
    {
        var context = NewContext();
        var collectorId = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-10", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2024, 7, 1), 3));

        var paid = DailyCollection.Create(stall.Id, new DateOnly(2026, 7, 3));
        paid.MarkPaid("OR-1", collectorId);
        context.Add(paid);
        await context.SaveChangesAsync();

        var repo = new CollectorRepository(context);
        var report = await repo.GetCollectorReportAsync(
            collectorId, new[] { FacilityCode.NPM }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);

        var txn = Assert.Single(report.Transactions);
        Assert.Equal("OR-1", txn.ORNumber);
        Assert.Equal(new DateOnly(2026, 7, 3), txn.CollectionDate);
        Assert.Equal(FeeRates.NpmDailyFee, txn.Amount);
        Assert.False(txn.IsPartial);
    }

    [Fact]
    public async Task CollectorRecords_CollectedAt_IsPhilippineTime_NotUtc()
    {
        var context = NewContext();
        var collectorId = Guid.NewGuid();
        // CreatedAt is stamped UTC now; the record feed must display it as Philippine wall-clock (UTC+8).
        var tx = SlaughterTransaction.CreateHog(Guid.NewGuid(), collectorId, "Owner", 1, "OR-9", PhilippineTime.Today);
        context.Add(tx);
        await context.SaveChangesAsync();
        var utcNow = DateTime.UtcNow;

        var repo = new CollectorRepository(context);
        var records = await repo.GetCollectorRecordsAsync(
            collectorId, FacilityCode.SLH, PhilippineTime.Today, PhilippineTime.Today, CancellationToken.None);

        var rec = Assert.Single(records);
        // CollectedAt must be ~UTC+8 (Philippine time), not the raw UTC timestamp.
        var deltaHours = (rec.CollectedAt - utcNow).TotalHours;
        Assert.InRange(deltaHours, 7.5, 8.5);
    }

    // ── Hardening: contract-period precision when multiple / future / expired contracts exist ──

    [Fact]
    public async Task MonthlyMobileCollection_WithFutureAndCurrentContracts_UsesTheCoveringContract()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "401", 2400m, ApplicableFees.BaseRental);
        // Current contract covers June 2026; a future one starts August 2026 (LATER effectivity).
        var current = Contract.Create(stall.Id, "June Payor", "June Payor", new DateOnly(2024, 6, 1), 3, 2400m);
        var future = Contract.Create(stall.Id, "Future Payor", "Future Payor", new DateOnly(2026, 8, 1), 3, 2400m);
        context.AddRange(facility, stall, current, future);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var result = await repo.GetMobileMonthlyCollectionAsync(FacilityCode.TCC, 2026, 6, new DateOnly(2026, 6, 15), CancellationToken.None);

        var row = Assert.Single(result.Stalls);
        Assert.Equal("June Payor", row.PayorName); // NOT the later-effectivity "Future Payor"
    }

    [Fact]
    public async Task NpmMobileCollection_ContractExpiredMidMonth_IsNotPendingToday_AfterExpiry()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-20", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        context.AddRange(facility, stall, ContractExpiring(stall.Id, new DateOnly(2023, 7, 10), 3)); // expires 2026-07-10
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        // Viewing July 20 — after the mid-month expiry.
        var result = await repo.GetMobileNpmCollectionAsync(2026, 7, new DateOnly(2026, 7, 20), CancellationToken.None);

        var row = Assert.Single(result.Stalls);
        Assert.Equal(10, row.CollectableDays);   // still owes Jul 1–10
        Assert.False(row.IsCollectableToday);    // but NOT a today target after expiry
        Assert.Equal(0, result.PendingTodayCount);
    }

    [Fact]
    public async Task CollectorReport_AbsenceOutsideContractTerm_IsNotCountedExcused()
    {
        var context = NewContext();
        var collectorId = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "V-21", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Contract starts mid-month (July 15).
        context.AddRange(facility, stall, Contract.Create(stall.Id, "Payor", "Payor", new DateOnly(2026, 7, 15), 3, 900m));
        var before = DailyCollection.Create(stall.Id, new DateOnly(2026, 7, 5)); before.MarkAbsent("Head");   // before term
        var within = DailyCollection.Create(stall.Id, new DateOnly(2026, 7, 20)); within.MarkAbsent("Head");  // in term
        context.AddRange(before, within);
        await context.SaveChangesAsync();

        var repo = new CollectorRepository(context);
        var report = await repo.GetCollectorReportAsync(
            collectorId, new[] { FacilityCode.NPM }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);

        Assert.Equal(1, report.Totals.AbsentExcusedCount);  // only the in-term July 20 absence
        var row = Assert.Single(report.AbsentExcused);
        Assert.Equal(new DateOnly(2026, 7, 20), row.Date);
    }
}
