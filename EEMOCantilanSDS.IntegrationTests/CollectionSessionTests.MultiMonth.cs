using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class CollectionSessionTests
{
    /// <summary>
    /// A collector who chooses several owed months of one space account sends one item per month. Each is validated and posted on its own against
    /// that month's own balance: the quote states exactly what is posted, a part payment leaves the rest of that month owing, a month cannot be
    /// taken twice in one checkout or taken for more than it owes, and a retry of the same checkout posts nothing more.
    /// </summary>
    [SkippableFact]
    public async Task ChoosingSeveralMonthsOfASpaceAccount_PostsEachAtItsOwnBalance_ARetryPostsNothingMore_AndNoMonthIsTakenTwiceOrForMoreThanItOwes()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var classification = RevenueClassification.Create(ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, Today.AddYears(-1), "Space", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.KanmanggaySpaceRental, "head"));
        await db.SaveChangesAsync();

        // One account, ₱1,000 a month from April; April-September have elapsed on the test's business date (Oct 5, 2026).
        var account = (await new ObligationWorkflow(db, head, tenant, new Clock()).CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, w.PayorId, null,
            "", null, null, new DateOnly(Today.Year, 4, 1), 1_000m, OccupancyArrangement.SpaceOnly))).Value!;
        var collector = new Caller(w.CollectorId, w.TenantId);
        var flow = Workflow(db, w, new CollectionSessionSources(db, collector, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays()));
        CollectionSessionItemIntent Month(int month, decimal amount, Guid? id = null) =>
            new(id ?? Guid.NewGuid(), CollectionSessionItemKind.Obligation, amount, Obligation: new(account.Id, Today.Year, month));
        async Task<IReadOnlyList<ObligationQuoteDto>> RegisterAsync() =>
            (await new ObligationWorkflow(db, head, tenant, new Clock()).GetRegisterAsync(account.Id)).Value!;

        // 1. Three months chosen: April in full, May part-paid, June in full. The quote states exactly what will be posted.
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [Month(4, 1_000m), Month(5, 400m), Month(6, 1_000m)]);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join("; ", quote.Problems.Select(p => p.Message)));
        Assert.Equal(2_400m, quote.GrandTotal);

        var posted = await Record(flow, intent);
        Assert.Equal(quote.GrandTotal, posted.GrandTotal);                                                    // quote equals what was posted
        Assert.Equal(3, await db.Collections.CountAsync());                                                   // one collection per month

        var after = await RegisterAsync();
        decimal Owing(int month) => after.Single(p => p.PeriodStart == new DateOnly(Today.Year, month, 1)).OutstandingAmount;
        Assert.Equal((0m, 600m, 0m), (Owing(4), Owing(5), Owing(6)));                                         // each month by its own amount
        Assert.Equal(1_000m, Owing(7));                                                                       // a month not chosen is untouched
        var allocations = await (from a in db.CollectionAllocations
                                 join p in db.ObligationPeriods on a.SourceId equals p.Id
                                 where p.ObligationAccountId == account.Id
                                 select new { p.PeriodStart, a.Amount }).ToListAsync();
        Assert.Equal([(4, 1_000m), (5, 400m), (6, 1_000m)], allocations.OrderBy(x => x.PeriodStart).Select(x => (x.PeriodStart.Month, x.Amount)).ToArray());

        // 2. The same checkout retried (a dropped answer, an offline resend) posts nothing more.
        var replay = await flow.RecordAsync(new(intent, quote.QuoteFingerprint));
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(3, await db.Collections.CountAsync());
        Assert.Equal(3, await db.CollectionAllocations.CountAsync());

        // 3. Taking May again for more than the ₱600 it still owes is refused, and so is the same month twice in one checkout.
        var overBalance = (await flow.QuoteAsync(new(Guid.NewGuid(), Today, w.PayorId, [Month(5, 700m)]))).Value!;
        Assert.False(overBalance.CanRecord);
        var twice = (await flow.QuoteAsync(new(Guid.NewGuid(), Today, w.PayorId, [Month(7, 500m), Month(7, 500m)]))).Value!;
        Assert.False(twice.CanRecord);

        // 4. Removing an item before recording is just a smaller checkout: the remaining months quote and post alone.
        var smaller = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [Month(5, 600m), Month(7, 1_000m)]);
        Assert.Equal(1_600m, (await Record(flow, smaller)).GrandTotal);
        after = await RegisterAsync();
        Assert.Equal((0m, 0m, 0m, 0m), (Owing(4), Owing(5), Owing(6), Owing(7)));
        Assert.Equal(1_000m, Owing(8));
        Assert.Equal(5, await db.Collections.CountAsync());
    }
}
