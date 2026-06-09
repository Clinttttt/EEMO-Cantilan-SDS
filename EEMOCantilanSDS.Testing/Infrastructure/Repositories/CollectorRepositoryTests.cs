using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class CollectorRepositoryTests : RepositoryTestBase
{
    // Regression for P1 (daily collections keyed by CollectorId, not CreatedBy) and
    // P3 (a Partial payment is counted at PartialAmount, not the full bill).
    [Fact]
    public async Task GetAllCollectorsWithStats_CountsPartialAtPartialAmount_AndDailyByCollectorId()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");
        var stallId = Guid.NewGuid();

        var payment = PaymentRecord.Create(stallId, 2026, 1, baseRental: 900m);
        payment.UpdateStatus(PaymentStatus.Partial, partialAmount: 300m, remarks: null, updatedBy: "t", collectorId: collector.Id);

        var daily = DailyCollection.Create(stallId, new DateOnly(2026, 1, 15));
        daily.MarkPaid(orNumber: "", collectorId: collector.Id);

        ctx.Add(collector);
        ctx.Add(payment);
        ctx.Add(daily);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var stats = await repo.GetAllCollectorsWithStatsAsync(2026, 1);

        var dto = Assert.Single(stats);
        Assert.Equal(330m, dto.CollectedThisMonth); // 300 (partial) + 30 (daily fee) — NOT 900
        Assert.Equal(2, dto.Transactions);          // 1 payment + 1 daily collection
    }

    [Fact]
    public async Task GetAllCollectorsWithStats_IgnoresOtherCollectorsCollections()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Juan", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");
        var otherCollectorId = Guid.NewGuid();

        var theirs = PaymentRecord.Create(Guid.NewGuid(), 2026, 1, 900m);
        theirs.UpdateStatus(PaymentStatus.Paid, 0m, null, "t", otherCollectorId);

        ctx.Add(collector);
        ctx.Add(theirs);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var dto = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(2026, 1));

        Assert.Equal(0m, dto.CollectedThisMonth);
        Assert.Equal(0, dto.Transactions);
    }

    // Regression: collectors assigned to per-transaction facilities (SLH/TRM/TPM) previously
    // showed ₱0 / 0 because only PaymentRecords + DailyCollections were aggregated.
    [Fact]
    public async Task GetAllCollectorsWithStats_IncludesSlaughterTripAndMarketCollections()
    {
        await using var ctx = NewContext();
        var today = PhilippineTime.Today;

        var collector = CollectorUser.Create("Pedro Cruz", "EEMO-2026-009", "pedro", "pedro@x.com", "0917", "pw");

        // SLH: Hog ×1 = ₱250 (TransactionDate carries the period)
        var slh = SlaughterTransaction.CreateHog(Guid.NewGuid(), collector.Id, "Owner A", 1, "OR-S1", today);

        // TRM: one trip = ₱30 (RecordedAt = UtcNow → current month)
        var trip = TrmTrip.Create(Guid.NewGuid(), 1, "Driver A", "ABC 123", "Route 1", "OR-T1", collectorId: collector.Id);

        // TPM: one paid vendor = ₱100 on a Friday in the current month
        var friday = new DateOnly(today.Year, today.Month, 1);
        while (friday.DayOfWeek != DayOfWeek.Friday) friday = friday.AddDays(1);
        var tpm = TpmAttendance.Create(Guid.NewGuid(), friday);
        tpm.MarkPaid(collector.Id);

        ctx.Add(collector);
        ctx.Add(slh);
        ctx.Add(trip);
        ctx.Add(tpm);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var dto = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(today.Year, today.Month));

        Assert.Equal(380m, dto.CollectedThisMonth); // 250 (SLH) + 30 (TRM) + 100 (TPM)
        Assert.Equal(3, dto.Transactions);
    }
}
