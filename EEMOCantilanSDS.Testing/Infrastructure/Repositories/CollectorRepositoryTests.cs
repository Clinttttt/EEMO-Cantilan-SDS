using EEMOCantilanSDS.Domain.Entities.Payments;
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
}
