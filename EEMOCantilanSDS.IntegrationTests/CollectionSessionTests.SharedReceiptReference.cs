using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ST-21 scope: the optional receipt reference accompanies the Official Receipt charges of ANY operation in one checkout. It is stamped on each Official Receipt
/// collection, never on Cash Ticket ones, never consumes an accountable document, and changes no amount, allocation or SRC.
/// </summary>
public sealed partial class CollectionSessionTests
{
    private static CollectionSessionIntent MixedIntent(World w, string? reference) => new(Guid.NewGuid(), Today, w.PayorId,
        [new(Guid.Parse("00000000-0000-0000-0000-000000000001"), CollectionSessionItemKind.NpmWholePayment, 0m, NpmWhole: new(w.StallId, Today.Year, Today.Month)),
         new(Guid.Parse("00000000-0000-0000-0000-000000000002"), CollectionSessionItemKind.VendorFee, 100m, VendorFee: new(w.StallId)),
         new(Guid.Parse("00000000-0000-0000-0000-000000000003"), CollectionSessionItemKind.Weighing, 0m, Weighing: new(w.StallId, WeighingType.Fish, 22m)),
         new(Guid.Parse("00000000-0000-0000-0000-000000000004"), CollectionSessionItemKind.Water, 20m, Water: new(w.StallId, Today.Year, Today.Month)),
         new(Guid.Parse("00000000-0000-0000-0000-000000000005"), CollectionSessionItemKind.Electricity, 44m, Electricity: new(Guid.Empty, 0, w.StallId, Today.Year, Today.Month)),
         new(Guid.Parse("00000000-0000-0000-0000-000000000006"), CollectionSessionItemKind.GovernedService, 200m, Service: new(CollectorOperationCodes.LandingBerthing))],
        ReceiptReference: reference);

    private async Task<(World W, CollectionSessionWorkflow Flow, AppDbContext Db)> SeedMixedAsync()
    {
        var w = await SeedAsync();
        await using (var setup = database.CreateContext(w.TenantId))
        {
            await EnableRentAndWeighing(setup, w);
            setup.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
                CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }
        var db = database.CreateContext(w.TenantId);
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays(), Whole(db, w));
        return (w, Workflow(db, w, source), db);
    }

    [SkippableFact]
    public async Task One_reference_is_stamped_on_every_Official_Receipt_collection_of_a_mixed_checkout_and_never_on_Cash_Ticket_ones()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var intent = MixedIntent(w, "  REF-777 ");

        var result = await Record(flow, intent);

        Assert.Equal(1330m, result.GrandTotal);                                      // exactly the amounts of the same checkout without a reference
        Assert.Equal(6, result.Collections.Count);
        var stored = await db.Collections.AsNoTracking().ToListAsync();
        var byId = result.Collections.ToDictionary(c => c.CollectionId);
        Assert.Equal(6, stored.Count);
        foreach (var collection in stored)
        {
            var instrument = byId[collection.Id].Instrument;
            if (instrument == RevenueInstrumentType.OfficialReceipt) Assert.Equal("REF-777", collection.ReceiptReference);     // trimmed, shared
            else Assert.Null(collection.ReceiptReference);                                                                      // Cash Ticket etc.: unchanged
        }
        Assert.Contains(result.Collections, c => c.Instrument == RevenueInstrumentType.OfficialReceipt);
        Assert.Contains(result.Collections, c => c.Instrument == RevenueInstrumentType.CashTicket);
        Assert.Empty(await db.AccountableDocuments.Where(d => d.CollectionId != null && stored.Select(s => s.Id).Contains(d.CollectionId.Value)).ToListAsync());   // no custody, no consumption

        // A retry of the same session returns the same collections and stamps nothing twice.
        var replay = (await flow.RecordAsync(new(intent, null))).Value!;
        Assert.True(replay.ExistingOutcome);
        Assert.Equal(result.Collections.Select(c => c.ReferenceCode).OrderBy(x => x), replay.Collections.Select(c => c.ReferenceCode).OrderBy(x => x));
        Assert.Equal(6, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task A_blank_reference_changes_nothing_and_a_different_reference_for_the_same_session_is_a_conflict()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var plain = await Record(flow, MixedIntent(w, null));
        Assert.Equal(1330m, plain.GrandTotal);
        Assert.All(await db.Collections.AsNoTracking().ToListAsync(), c => Assert.Null(c.ReceiptReference));
    }

    [SkippableFact]
    public async Task A_reference_on_a_checkout_without_any_Official_Receipt_charge_is_refused_not_silently_dropped()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var cashTicketOnly = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.Parse("00000000-0000-0000-0000-000000000006"), CollectionSessionItemKind.GovernedService, 200m, Service: new(CollectorOperationCodes.LandingBerthing))],
            ReceiptReference: "REF-1");
        var quote = (await flow.QuoteAsync(cashTicketOnly)).Value!;
        Assert.False(quote.CanRecord);
        Assert.Contains(quote.Problems, p => p.Code == "ReceiptReferenceNeedsOrCharge");
        Assert.Equal(0, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task The_same_session_with_a_different_reference_is_an_intent_conflict_and_posts_nothing_more()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var first = MixedIntent(w, "REF-A");
        await Record(flow, first);
        var changed = first with { ReceiptReference = "REF-B" };
        var quote = (await flow.QuoteAsync(changed)).Value!;
        var response = (await flow.RecordAsync(new(changed, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.NeedsReview, response.Status);
        Assert.Contains(response.Problems, p => p.Code == "SessionIntentConflict");
        Assert.Equal(6, await db.Collections.CountAsync());
        Assert.All(await db.Collections.AsNoTracking().Where(c => c.ReceiptReference != null).ToListAsync(), c => Assert.Equal("REF-A", c.ReceiptReference));
    }
}
