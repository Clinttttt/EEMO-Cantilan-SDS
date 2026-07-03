using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// The mobile Records feed (<see cref="CollectorRepository.GetCollectorRecordsAsync"/>) must return only
/// the authenticated collector's OWN collection events, scoped by facility + PH date range, with the
/// correct paid/partial amounts. These lock in isolation, filtering, and money accuracy.
/// </summary>
public class CollectorRecordsTests : RepositoryTestBase
{
    private static readonly DateOnly Today = PhilippineTime.Today;

    [Fact]
    public async Task ReturnsOwnRecordsOnly_AndRespectsFacilityFilter()
    {
        await using var ctx = NewContext();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();

        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Owner A", 1, "OR-A", Today));     // mine
        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), other, "Owner B", 1, "OR-B", Today));  // someone else's
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        var all = await repo.GetCollectorRecordsAsync(me, null, Today, Today, CancellationToken.None);
        var mine = Assert.Single(all);
        Assert.Equal("Owner A", mine.PayorName);
        Assert.Equal(FacilityCode.SLH, mine.FacilityCode);
        Assert.Equal(250m, mine.Amount);
        Assert.False(mine.IsPartial);

        Assert.Single(await repo.GetCollectorRecordsAsync(me, FacilityCode.SLH, Today, Today, CancellationToken.None));
        Assert.Empty(await repo.GetCollectorRecordsAsync(me, FacilityCode.TRM, Today, Today, CancellationToken.None));
    }

    [Fact]
    public async Task Slaughter_GroupsMultipleAnimalsUnderOneReceipt()
    {
        // One slaughter OR covers a customer's whole visit — several animal rows under the same OR are
        // ONE receipt and must render as a single feed card, with the per-animal breakdown preserved.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Jericho Rosales", 1, "OR-464773", Today));
        ctx.Add(SlaughterTransaction.CreateLargeAnimal(Guid.NewGuid(), me, "Jericho Rosales", AnimalType.Carabao, 1, "OR-464773", Today));
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var records = await repo.GetCollectorRecordsAsync(me, FacilityCode.SLH, Today, Today, CancellationToken.None);

        var receipt = Assert.Single(records);                       // ONE card, not two
        Assert.Equal("Jericho Rosales", receipt.PayorName);
        Assert.Equal("OR-464773", receipt.ORNumber);
        Assert.NotNull(receipt.SlaughterLines);
        Assert.Equal(2, receipt.SlaughterLines!.Count);             // both animal lines preserved for the popup
        Assert.Equal(receipt.Amount, receipt.SlaughterLines.Sum(l => l.Amount)); // card total == sum of lines
        Assert.Equal(receipt.Amount, receipt.AmountPaid);
    }

    [Fact]
    public async Task RespectsDateRange()
    {
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Recent", 1, "OR-1", Today));
        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Old", 1, "OR-2", Today.AddDays(-40)));
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        var todayOnly = await repo.GetCollectorRecordsAsync(me, null, Today, Today, CancellationToken.None);
        Assert.Equal("Recent", Assert.Single(todayOnly).PayorName);

        var oldDay = Today.AddDays(-40);
        var oldOnly = await repo.GetCollectorRecordsAsync(me, null, oldDay, oldDay, CancellationToken.None);
        Assert.Equal("Old", Assert.Single(oldOnly).PayorName);
    }

    [Fact]
    public async Task MonthlyPartialPayment_MapsFullAndCollectedAmounts()
    {
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "B-1", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2025, 1, 1), 3, 2400m);
        var payment = PaymentRecord.Create(stall.Id, Today.Year, Today.Month, 2400m);
        payment.UpdateStatus(PaymentStatus.Partial, 1000m, null, "tester", me);

        ctx.AddRange(facility, stall, contract, payment);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var rec = Assert.Single(await repo.GetCollectorRecordsAsync(me, null, monthStart, Today, CancellationToken.None));

        Assert.Equal("Maria Santos", rec.PayorName);
        Assert.Equal(FacilityCode.TCC, rec.FacilityCode);
        Assert.True(rec.IsPartial);
        Assert.Equal(2400m, rec.Amount);       // full bill
        Assert.Equal(1000m, rec.AmountPaid);   // collected
    }

    [Fact]
    public async Task CollectorReport_NpmIncludesMonthlyPayments_AndDoesNotDoubleCountDailyRowsForSameStall()
    {
        await using var ctx = NewContext();
        var collectorId = Guid.NewGuid();
        var reportDay = new DateOnly(Today.Year, Today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var monthlyStall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var dailyStall = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var missedStall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(monthlyStall, facility);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(dailyStall, facility);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(missedStall, facility);
        var monthlyContract = Contract.Create(monthlyStall.Id, "Monthly Payor", "Monthly Payor", reportDay.AddMonths(-1), 3, 900m);
        var dailyContract = Contract.Create(dailyStall.Id, "Daily Payor", "Daily Payor", reportDay.AddMonths(-1), 3, 900m);
        var missedContract = Contract.Create(missedStall.Id, "Unpaid Payor", "Unpaid Payor", reportDay.AddMonths(-1), 3, 900m);
        monthlyStall.Contracts.Add(monthlyContract);
        dailyStall.Contracts.Add(dailyContract);
        missedStall.Contracts.Add(missedContract);

        var monthlyPayment = PaymentRecord.Create(monthlyStall.Id, reportDay.Year, reportDay.Month, 900m);
        monthlyPayment.RecordPayment("OR-MONTHLY", collectorId, PaymentStatus.Partial, partialAmount: 80m);

        var duplicateDaily = DailyCollection.Create(monthlyStall.Id, reportDay);
        duplicateDaily.MarkPaid("OR-DAILY-DUP", collectorId);
        monthlyStall.DailyCollections.Add(duplicateDaily);

        var dailyCollection = DailyCollection.Create(dailyStall.Id, reportDay);
        dailyCollection.MarkPaid("OR-DAILY", collectorId);
        dailyStall.DailyCollections.Add(dailyCollection);

        ctx.AddRange(
            facility,
            monthlyStall, dailyStall, missedStall,
            monthlyContract, dailyContract, missedContract,
            monthlyPayment, duplicateDaily, dailyCollection);
        await ctx.SaveChangesAsync();

        Assert.Equal(3, await ctx.Stalls
            .Where(s => s.Facility!.Code == FacilityCode.NPM && s.Contracts.Any(c => c.IsActive))
            .CountAsync());
        Assert.Single(await ctx.PaymentRecords
            .Where(p => p.StallId == monthlyStall.Id && p.Status == PaymentStatus.Partial)
            .ToListAsync());
        var npmStallIds = await ctx.Stalls
            .AsNoTracking()
            .Where(s => s.Facility!.Code == FacilityCode.NPM && s.Contracts.Any(c => c.IsActive))
            .Select(s => s.Id)
            .ToListAsync();
        Assert.Single(await ctx.PaymentRecords
            .AsNoTracking()
            .Where(p => npmStallIds.Contains(p.StallId))
            .ToListAsync());

        var repo = new CollectorRepository(ctx);
        var report = await repo.GetCollectorReportAsync(
            collectorId,
            [FacilityCode.NPM],
            reportDay,
            reportDay.AddDays(2),
            CancellationToken.None);

        Assert.Equal(110m, report.Totals.CollectedAmount); // monthly partial ₱80 + one daily ₱30
        Assert.Equal(2, report.Totals.TransactionCount);

        var period = Assert.Single(report.Periods, p => p.PeriodDate == reportDay);
        Assert.Equal(30m, period.CollectedAmount);
        Assert.Equal(1, period.OpenItemCount);
    }

    [Fact]
    public async Task IncludesAdminRecordedAtAssignedFacility_AndFlagsThem()
    {
        // The Records feed shows the collector's own collections PLUS admin/office-recorded entries
        // (CollectorId == null) at the facilities they're assigned to, tagged via IsAdminRecorded.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall1 = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract1 = Contract.Create(stall1.Id, "Pedro", "Pedro", Today.AddMonths(-1), 3, 900m);
        var stall2 = Stall.Create(npm.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract2 = Contract.Create(stall2.Id, "Maria", "Maria", Today.AddMonths(-1), 3, 900m);

        var assignment = CollectorFacilityAssignment.Create(me, npm.Id, FacilityCode.NPM);

        var mine = DailyCollection.Create(stall1.Id, Today);
        mine.MarkPaid("OR-MINE", me);                 // collected by this collector

        var adminDaily = DailyCollection.Create(stall2.Id, Today);
        adminDaily.MarkPaid("", null);                // recorded by office/admin → CollectorId null

        ctx.AddRange(npm, stall1, stall2, contract1, contract2, assignment, mine, adminDaily);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var records = await repo.GetCollectorRecordsAsync(me, FacilityCode.NPM, Today, Today, CancellationToken.None);

        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.PayorName == "Pedro" && !r.IsAdminRecorded);
        Assert.Contains(records, r => r.PayorName == "Maria" && r.IsAdminRecorded);
    }

    [Fact]
    public async Task ExcludesAdminRecordedAtUnassignedFacility()
    {
        // Admin-recorded entries are only surfaced at the collector's ASSIGNED facilities — an
        // office-recorded NPM collection must not leak to a collector not assigned to NPM.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pedro", "Pedro", Today.AddMonths(-1), 3, 900m);

        // Assigned to TRM only — not NPM.
        var assignment = CollectorFacilityAssignment.Create(me, Guid.NewGuid(), FacilityCode.TRM);

        var adminDaily = DailyCollection.Create(stall.Id, Today);
        adminDaily.MarkPaid("", null);

        ctx.AddRange(npm, stall, contract, assignment, adminDaily);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var npmRecords = await repo.GetCollectorRecordsAsync(me, FacilityCode.NPM, Today, Today, CancellationToken.None);

        Assert.Empty(npmRecords);
    }

    [Fact]
    public async Task Report_Npm_CountsAdvancePaidDays_AndUsesBusinessLastCollectionDate()
    {
        // Obligation is assessed only to "today", but collected money includes advance-paid days;
        // "last collection" must be the latest business CollectionDate, not the record timestamp.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();
        var monthStart = new DateOnly(2026, 6, 1);
        var simulatedToday = new DateOnly(2026, 6, 19);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall, facility);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 6, 5), 3, 900m);
        stall.Contracts.Add(contract);

        var collections = new List<DailyCollection>();
        for (var day = 5; day <= 20; day++) // Jun 5–20: 16 paid days; day 20 is AFTER "today" (advance)
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            dc.MarkPaid($"OR-{day}", me);
            stall.DailyCollections.Add(dc);
            collections.Add(dc);
        }

        ctx.AddRange(facility, stall, contract);
        ctx.AddRange(collections);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var report = await repo.GetCollectorReportAsync(me, [FacilityCode.NPM], monthStart, simulatedToday, CancellationToken.None);

        var payee = Assert.Single(report.Payees);
        Assert.Equal(16, payee.TransactionCount);                  // advance day 20 counted
        Assert.Equal(480m, payee.AmountPaid);                      // 16 × ₱30 rental
        Assert.Equal(780m, payee.AssessedAmount);                  // FULL month obligation (Jun 5–30 = 26 × ₱30), matches web
        Assert.Equal(300m, payee.Balance);                         // 780 − 480
        Assert.Equal(PaymentStatus.Partial, payee.Status);         // still owes → Partial (counts toward Partial Payors)
        Assert.Equal(new DateOnly(2026, 6, 20), DateOnly.FromDateTime(payee.LastCollectedAt!.Value)); // business date
    }

    [Fact]
    public async Task Report_Npm_ExcludesOtherCollectors_IncludesOfficeAdmin()
    {
        // Collector-own report: the collector's own + office/admin (null) collections count; another
        // collector's collections at the same stall are excluded.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var monthStart = new DateOnly(2026, 6, 1);
        var simulatedToday = new DateOnly(2026, 6, 19);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall, facility);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 6, 5), 3, 900m);
        stall.Contracts.Add(contract);

        var officeDay = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 5));
        officeDay.MarkPaid("OR-OFFICE", null);            // office/admin → included
        var myDay = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6));
        myDay.MarkPaid("OR-ME", me);                       // mine → included
        var otherDay = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 7));
        otherDay.MarkPaid("OR-OTHER", other);              // another collector → EXCLUDED
        stall.DailyCollections.Add(officeDay);
        stall.DailyCollections.Add(myDay);
        stall.DailyCollections.Add(otherDay);

        ctx.AddRange(facility, stall, contract, officeDay, myDay, otherDay);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var report = await repo.GetCollectorReportAsync(me, [FacilityCode.NPM], monthStart, simulatedToday, CancellationToken.None);

        var payee = Assert.Single(report.Payees);
        Assert.Equal(2, payee.TransactionCount);           // office + mine; other-collector excluded
        Assert.Equal(60m, payee.AmountPaid);               // 2 × ₱30
        Assert.Equal(2, report.Totals.TransactionCount);
        Assert.Equal(60m, report.Totals.CollectedAmount);
    }

}
