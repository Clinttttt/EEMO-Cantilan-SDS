using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Reports;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>ST-21: one physical Official Receipt for several explicit months of ONE space account = one Collection, one line per month.</summary>
public sealed partial class CollectionSessionTests
{
    private sealed record OrWorld(World W, AppDbContext Db, Caller Head, Tenant TenantRef, CollectionSessionWorkflow Flow, AccountableDocument[] Docs, Guid AccountId);

    private async Task<OrWorld> SeedOrAsync(DateOnly start, int documents = 3)
    {
        var w = await SeedAsync();
        var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var classification = RevenueClassification.Create(ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), w.TenantId);
        var book = AccountableFormBook.Receive(w.TenantId, RevenueInstrumentType.OfficialReceipt, "Synthetic OR", "OR-", 1, 99, 4, DateTime.UtcNow.AddMinutes(-5), head.Id.ToString("N"), "test");
        var docs = Enumerable.Range(1, documents).Select(i => AccountableDocument.Register(book, i, "test")).ToArray();
        foreach (var d in docs) d.AssignTo(w.CollectorId, "test");
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, Today.AddYears(-3), "Space", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.KanmanggaySpaceRental, "head"), book);
        db.AddRange(docs);
        await db.SaveChangesAsync();
        var account = (await new ObligationWorkflow(db, head, tenant, new Clock()).CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, w.PayorId, null,
            "", null, null, start, 1_000m, OccupancyArrangement.SpaceOnly))).Value!;
        var flow = Workflow(db, w, new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays()));
        return new(w, db, head, tenant, flow, docs, account.Id);
    }

    private static CollectionSessionItemIntent OrMonth(OrWorld s, int year, int month, decimal amount) =>
        new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, amount, Obligation: new(s.AccountId, year, month));

    private static CollectionSessionIntent OrIntent(OrWorld s, string? or, params CollectionSessionItemIntent[] items) =>
        new(Guid.NewGuid(), Today, s.W.PayorId, items, OfficialReceiptNumber: or);

    private static async Task<IReadOnlyList<ObligationQuoteDto>> OrRegisterAsync(OrWorld s) =>
        (await new ObligationWorkflow(s.Db, s.Head, s.TenantRef, new Clock()).GetRegisterAsync(s.AccountId)).Value!;

    [SkippableFact]
    public async Task OneOr_ThreeMonths_PostsOneCollectionWithALinePerMonth_ConsumesTheOrOnce_AndReplayAndConflictAreSafe()
    {
        var s = await SeedOrAsync(new DateOnly(Today.Year, 4, 1));
        await using var db = s.Db;
        var items = new[] { OrMonth(s, Today.Year, 4, 1_000m), OrMonth(s, Today.Year, 5, 400m), OrMonth(s, Today.Year, 6, 1_000m) };   // May is a part payment
        var intent = OrIntent(s, "OR-0001", items);
        var quote = (await s.Flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join("; ", quote.Problems.Select(p => p.Message)));
        var recorded = await Record(s.Flow, intent);

        var only = Assert.Single(recorded.Collections);                                             // ONE collection
        Assert.Equal(2_400m, only.Amount);
        Assert.Equal(items.Select(i => i.ClientItemId).OrderBy(x => x), only.ClientItemIds.OrderBy(x => x));
        var collection = await db.Collections.Include(c => c.Lines).ThenInclude(l => l.Allocations).SingleAsync();
        Assert.Equal(only.ReferenceCode, collection.ReferenceCode);
        Assert.Equal(3, collection.Lines.Count);
        var periods = await db.ObligationPeriods.ToDictionaryAsync(p => p.Id, p => p.PeriodStart);
        Assert.Equal([(4, 1_000m), (5, 400m), (6, 1_000m)], collection.Lines.Select(l => (periods[Assert.Single(l.Allocations).SourceId].Month, l.Amount)).OrderBy(x => x.Item1).ToArray());
        Assert.All(collection.Lines, l => Assert.Equal(Assert.Single(l.Allocations).Amount, l.Amount));
        Assert.Equal(1, collection.Lines.Select(l => l.RevenueClassificationId).Distinct().Count());

        // The OR is consumed exactly once, by that one collection; the unused ORs stay with the collector.
        var docs = await db.AccountableDocuments.OrderBy(d => d.SerialNumber).ToListAsync();
        Assert.Equal((AccountableDocumentState.Consumed, collection.Id), (docs[0].State, docs[0].CollectionId));
        Assert.All(docs.Skip(1), d => Assert.Equal(AccountableDocumentState.Assigned, d.State));
        var operation = await db.PostingOperations.SingleAsync(o => o.CollectionId == collection.Id);
        Assert.Equal(docs[0].Id, operation.AccountableDocumentId);

        // Each month by its own amount; the part-paid month still owes the rest; a month not chosen is untouched.
        var after = await OrRegisterAsync(s);
        decimal Owing(int m) => after.Single(p => p.PeriodStart == new DateOnly(Today.Year, m, 1)).OutstandingAmount;
        Assert.Equal((0m, 600m, 0m, 1_000m), (Owing(4), Owing(5), Owing(6), Owing(7)));

        // Replay returns the same single SRC and posts nothing more; a changed OR on the same session is a conflict.
        var replay = (await s.Flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(only.ReferenceCode, Assert.Single(replay.Collections).ReferenceCode);
        var conflict = (await s.Flow.RecordAsync(new(intent with { OfficialReceiptNumber = "OR-0002" }, quote.QuoteFingerprint))).Value!;
        Assert.Contains(conflict.Problems, p => p.Code == "SessionIntentConflict");
        Assert.Equal((1, 3, 1), (await db.Collections.CountAsync(), await db.CollectionLines.CountAsync(), await db.AccountableDocuments.CountAsync(d => d.State == AccountableDocumentState.Consumed)));

        // The consumed OR can never be used again, and a month already paid in full cannot be taken again.
        var reuse = (await s.Flow.QuoteAsync(OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 7, 1_000m)))).Value!;
        Assert.False(reuse.CanRecord); Assert.Contains(reuse.Problems, p => p.Code == "ReceiptUnavailable");
        Assert.False((await s.Flow.QuoteAsync(OrIntent(s, "OR-0002", OrMonth(s, Today.Year, 4, 100m)))).Value!.CanRecord);

        // Collection Activity reads one SRC with the whole amount and the OR number.
        var activity = (await new CollectionActivityReader(db).GetAsync(s.W.TenantId, Today, Today)).Where(e => e.CollectionId == collection.Id).ToList();
        var row = Assert.Single(activity);
        Assert.Equal((only.ReferenceCode, 2_400m, docs[0].DocumentNumber), (row.ReferenceCode, row.Amount, row.DocumentNumber));
    }

    [SkippableFact]
    public async Task OneOr_TwelveMonths_IsOneCollectionOfTwelveLines()
    {
        var s = await SeedOrAsync(new DateOnly(Today.Year - 1, 10, 1), documents: 1);
        await using var db = s.Db;
        var months = Enumerable.Range(0, 12).Select(i => new DateOnly(Today.Year - 1, 10, 1).AddMonths(i)).Select(d => OrMonth(s, d.Year, d.Month, 1_000m)).ToArray();
        var recorded = await Record(s.Flow, OrIntent(s, "OR-0001", months));
        Assert.Equal(12_000m, Assert.Single(recorded.Collections).Amount);
        Assert.Equal((1, 12, 12), (await db.Collections.CountAsync(), await db.CollectionLines.CountAsync(), await db.CollectionAllocations.CountAsync()));
        Assert.Equal(1, await db.AccountableDocuments.CountAsync(d => d.State == AccountableDocumentState.Consumed));
    }

    [SkippableFact]
    public async Task WithoutAnOr_SeveralMonths_StayOneSrcPerMonth_AndConsumeNoDocument()
    {
        var s = await SeedOrAsync(new DateOnly(Today.Year, 4, 1));
        await using var db = s.Db;
        var recorded = await Record(s.Flow, OrIntent(s, null, OrMonth(s, Today.Year, 4, 1_000m), OrMonth(s, Today.Year, 5, 1_000m), OrMonth(s, Today.Year, 6, 500m)));
        Assert.Equal(3, recorded.Collections.Count);
        Assert.Equal((3, 3), (await db.Collections.CountAsync(), await db.CollectionLines.CountAsync()));
        Assert.Equal(0, await db.AccountableDocuments.CountAsync(d => d.State == AccountableDocumentState.Consumed));
    }

    [SkippableFact]
    public async Task OneOr_RefusesDuplicateMonths_UnregisteredOrs_OtherCollectorsOrs_AndOverBalance_WithNothingPosted()
    {
        var s = await SeedOrAsync(new DateOnly(Today.Year, 4, 1));
        await using var db = s.Db;
        Assert.False((await s.Flow.QuoteAsync(OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 4, 500m), OrMonth(s, Today.Year, 4, 500m)))).Value!.CanRecord);   // duplicate month
        var unknown = (await s.Flow.QuoteAsync(OrIntent(s, "OR-0077", OrMonth(s, Today.Year, 4, 500m), OrMonth(s, Today.Year, 5, 500m)))).Value!;
        Assert.Contains(unknown.Problems, p => p.Code == "ReceiptUnavailable");
        Assert.False((await s.Flow.QuoteAsync(OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 4, 1_001m), OrMonth(s, Today.Year, 5, 500m)))).Value!.CanRecord);   // over a month's balance
        s.Docs[0].TransferTo(Guid.NewGuid(), "test"); await db.SaveChangesAsync();                                                                             // custody belongs to someone else
        Assert.Contains((await s.Flow.QuoteAsync(OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 4, 500m)))).Value!.Problems, p => p.Code == "ReceiptUnavailable");
        Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(0, await db.AccountableDocuments.CountAsync(d => d.State == AccountableDocumentState.Consumed));
    }

    [SkippableFact]
    public async Task OneOr_MultiMonthCollection_CanBeVoidedByTheCollector_ReversingEveryLine_WhileTheOrStaysConsumed()
    {
        var s = await SeedOrAsync(new DateOnly(Today.Year, 4, 1));
        await using var db = s.Db;
        var collector = new Caller(s.W.CollectorId, s.W.TenantId);
        var recorded = await Record(s.Flow, OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 4, 1_000m), OrMonth(s, Today.Year, 5, 400m)));
        var id = Assert.Single(recorded.Collections).CollectionId;
        var mobile = new MobileCollectionCorrectionWorkflow(db, new CollectionSessionStore(db), null!, null!, new CollectionActivityReader(db), null!, collector, s.TenantRef, new Clock());
        var voided = await mobile.RemoveAsync(new(Guid.NewGuid(), id, new(MobileCorrectionReason.EnteredByMistake)));
        Assert.True(voided.IsSuccess, voided.Error);
        var correction = await db.CollectionCorrections.Include(c => c.Lines).SingleAsync();
        Assert.Equal(-1_400m, correction.Amount);
        Assert.Equal(2, correction.Lines.Count);
        var after = await OrRegisterAsync(s);
        Assert.Equal(1_000m, after.Single(p => p.PeriodStart == new DateOnly(Today.Year, 4, 1)).OutstandingAmount);          // balances are owed again
        Assert.Equal(1_000m, after.Single(p => p.PeriodStart == new DateOnly(Today.Year, 5, 1)).OutstandingAmount);
        Assert.Equal(AccountableDocumentState.Consumed, (await db.AccountableDocuments.SingleAsync(d => d.SerialNumber == 1)).State);   // a physical OR is never reused
        Assert.False((await s.Flow.QuoteAsync(OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 4, 1_000m)))).Value!.CanRecord);
    }

    [SkippableFact]
    public async Task OneOr_MultiMonthCollection_CanBeReversedByTheHead()
    {
        var s = await SeedOrAsync(new DateOnly(Today.Year, 4, 1));
        await using var db = s.Db;
        var recorded = await Record(s.Flow, OrIntent(s, "OR-0001", OrMonth(s, Today.Year, 4, 1_000m), OrMonth(s, Today.Year, 5, 1_000m), OrMonth(s, Today.Year, 6, 1_000m)));
        var head = new HeadCollectionCorrectionWorkflow(db, new CollectionSessionStore(db), s.Head, s.TenantRef, new Clock());
        var result = await head.ReverseAsync(new(Guid.NewGuid(), Assert.Single(recorded.Collections).CollectionId, "Wrong account"));
        Assert.True(result.IsSuccess, result.Error);
        var after = await OrRegisterAsync(s);
        Assert.All(new[] { 4, 5, 6 }, m => Assert.Equal(1_000m, after.Single(p => p.PeriodStart == new DateOnly(Today.Year, m, 1)).OutstandingAmount));
    }
}
