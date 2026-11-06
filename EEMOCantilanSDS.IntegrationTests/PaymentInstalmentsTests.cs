using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// IA-081 on PostgreSQL: a month paid in two receipts, in two different months, reports each receipt in the month it came in,
/// and the first receipt's OR stays used after the record shows the second.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PaymentInstalmentsTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task Finishing_a_partial_month_reports_each_receipt_in_its_own_month_and_keeps_the_first_OR()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var tenant = Municipality.Create("PARTS", "PARTS", "Province", MunicipalityStatus.Active, tenantCode: "parts");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(tenant);
            await setup.SaveChangesAsync();
        }

        var today = PhilippineTime.Today;
        var earlier = today.AddMonths(-1);
        var firstAt = PhilippineTime.MonthUtcRange(earlier.Year, earlier.Month).StartUtc.AddDays(9);
        Guid recordId, otherStall;
        var (firstCollector, secondCollector) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var hall = Facility.Create(FacilityCode.TCC, "Trade Hall", "TH", municipalityId: tenant.Id);
            var stall = Stall.Create(hall.Id, "P-01", 2_000m, ApplicableFees.BaseRental, municipalityId: tenant.Id);
            var other = Stall.Create(hall.Id, "P-02", 2_000m, ApplicableFees.BaseRental, municipalityId: tenant.Id);
            var month = PaymentRecord.Create(stall.Id, today.Year, today.Month, 2_000m, "test");
            month.RecordPayment("P-OR-1", firstCollector, PaymentStatus.Partial, 1_000m, updatedBy: "test");
            month.BackdateReceipt(firstAt, "test"); // the first receipt came in last month
            ctx.AddRange(hall, stall, other, month);
            await ctx.SaveChangesAsync();
            (recordId, otherStall) = (month.Id, other.Id);
        }

        // The rest is received today, through the repository the payment handlers use.
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var repository = new PaymentRepository(ctx);
            var month = await repository.GetByIdAsync(recordId, default);
            month!.RecordPayment("P-OR-2", secondCollector, PaymentStatus.Paid, updatedBy: "test");
            await repository.UpdateAsync(month, default);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var month = await ctx.PaymentRecords.AsNoTracking().SingleAsync(p => p.Id == recordId);
            Assert.Equal([(1_000m, "P-OR-1"), (1_000m, "P-OR-2")],
                month.Instalments.OrderBy(i => i.ReceivedAt).Select(i => (i.Amount, i.ORNumber)));
            Assert.Equal(firstAt, month.Instalments.Min(i => i.ReceivedAt));

            var reader = new LegacyMonthlyIncomeReader(ctx);
            decimal RentIn(IEnumerable<EEMOCantilanSDS.Application.Common.Interface.Persistence.LegacyIncomeFact> facts, int m) =>
                facts.Where(f => f.Source == "PaymentRecord" && f.Month == m).Sum(f => f.Amount);
            var earlierYear = await reader.GetAsync(earlier.Year);
            var thisYear = earlier.Year == today.Year ? earlierYear : await reader.GetAsync(today.Year);
            Assert.Equal(1_000m, RentIn(earlierYear, earlier.Month));
            Assert.Equal(1_000m, RentIn(thisYear, today.Month));

            Assert.False(await new PaymentRepository(ctx).IsMonthlyOrAvailableForStallAsync("P-OR-1", otherStall, default));

            // Each collector's report holds their own receipt, and the activity list shows both receipts, each on its own day.
            var from = new DateOnly(earlier.Year, earlier.Month, 1);
            var reports = new CollectorReportQueries(ctx);
            Assert.Equal([("P-OR-1", 1_000m)], (await reports.GetCollectionsAsync(firstCollector, from, today)).Lines.Select(l => (l.DocumentNumber, l.Amount)));
            Assert.Equal([("P-OR-2", 1_000m)], (await reports.GetCollectionsAsync(secondCollector, from, today)).Lines.Select(l => (l.DocumentNumber, l.Amount)));
            var activity = (await new CollectionActivityReader(ctx).GetAsync(tenant.Id, from, today))
                .Where(e => e.EventKey.StartsWith("PaymentRecord:")).OrderBy(e => e.BusinessDate).ToArray();
            Assert.Equal([("P-OR-1", DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(firstAt)), 1_000m), ("P-OR-2", today, 1_000m)],
                activity.Select(e => (e.DocumentNumber, e.BusinessDate, e.Amount)));
        }
    }
}
