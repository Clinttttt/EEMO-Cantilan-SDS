using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The office's first step before any back-entry: read-only evidence of whether the collection is already on the server. A missing row never
/// proves the money was not collected (the device may be unsynchronised), so it never reads as permission to post.
/// </summary>
public sealed partial class NpmDailyCanonicalTests
{
    private static BackEntryReconciliationReader Reader(AppDbContext ctx, World w, Actor actor) => new(ctx, actor, new FixedTenant(w.TenantId));

    [SkippableFact]
    public async Task BackEntryLookup_FindsAnExistingCollectionByOperationReferenceAndCollector_AndRecordsNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var operation = Guid.NewGuid();
        var posted = (await PayAsync(w, 20m, operationId: operation)).Value!;
        await using var ctx = db.CreateContext(w.TenantId);
        var reader = Reader(ctx, w, Admin(w));
        var before = (await ctx.Collections.CountAsync(), await ctx.PostingOperations.CountAsync(), await ctx.DailyCollections.CountAsync());

        var byOperation = (await reader.LookupAsync(new(ClientOperationId: operation))).Value!;
        Assert.Equal("AlreadyRecorded", byOperation.Verdict);
        Assert.Contains(byOperation.Matches, m => m.Kind == "Collection" && m.CollectionId == posted.CollectionId && m.Amount == 20m && m.Basis == "ClientOperationId");
        Assert.Contains(byOperation.Matches, m => m.Kind == "PostingOperation" && m.State == "Recorded");

        var byReference = (await reader.LookupAsync(new(ReferenceCode: " " + posted.ReferenceCode.ToLowerInvariant() + " "))).Value!;
        Assert.Equal("AlreadyRecorded", byReference.Verdict);
        Assert.Equal(posted.CollectionId, Assert.Single(byReference.Matches).CollectionId);

        // The same collector, day and amount is only a possible match: an officer compares it; it is not proof of a duplicate.
        var similar = (await reader.LookupAsync(new(CollectorId: w.CollectorId, BusinessDate: Today, Amount: 20m))).Value!;
        Assert.Equal("PossibleMatch", similar.Verdict);
        Assert.Equal("Mobile Collector", Assert.Single(similar.Matches).CollectorName);

        Assert.Equal(before, (await ctx.Collections.CountAsync(), await ctx.PostingOperations.CountAsync(), await ctx.DailyCollections.CountAsync()));
    }

    [SkippableFact]
    public async Task BackEntryLookup_ANoServerRecordIsNeverProofThatNothingWasCollected_AndCorrectionsAreStated()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var posted = (await PayAsync(w, 20m)).Value!;
        await using var ctx = db.CreateContext(w.TenantId);
        var reader = Reader(ctx, w, Admin(w));

        var unknown = (await reader.LookupAsync(new(ClientOperationId: Guid.NewGuid()))).Value!;
        Assert.Equal("NoServerRecord", unknown.Verdict);
        Assert.Empty(unknown.Matches);
        Assert.Contains("NOT proof", unknown.Statement);
        Assert.Contains("device", unknown.Statement);

        var wrongAmount = (await reader.LookupAsync(new(CollectorId: w.CollectorId, BusinessDate: Today, Amount: 21m))).Value!;
        Assert.Equal("NoServerRecord", wrongAmount.Verdict);

        Assert.True((await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(posted.CollectionId, "Wrong stall")).IsSuccess);
        var corrected = (await reader.LookupAsync(new(ReferenceCode: posted.ReferenceCode))).Value!;
        Assert.Equal("AlreadyRecorded", corrected.Verdict);                     // it was recorded, then corrected: still not to be entered again
        Assert.Equal("Corrected", Assert.Single(corrected.Matches).State);
    }

    [SkippableFact]
    public async Task BackEntryLookup_IsOfficeOnly_NeedsACriterion_AndNeverCrossesTenants()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var operation = Guid.NewGuid();
        Assert.True((await PayAsync(w, 20m, operationId: operation)).IsSuccess);
        await using var ctx = db.CreateContext(w.TenantId);

        Assert.Equal(ResultStatus.Forbidden, (await Reader(ctx, w, Collector(w)).LookupAsync(new(ClientOperationId: operation))).Status);
        Assert.Equal(ResultStatus.Invalid, (await Reader(ctx, w, Admin(w)).LookupAsync(new())).Status);
        Assert.Equal(ResultStatus.Invalid, (await Reader(ctx, w, Admin(w)).LookupAsync(new(CollectorId: w.CollectorId))).Status);   // a collector alone is not a criterion
        Assert.Equal(ResultStatus.Invalid, (await Reader(ctx, w, Admin(w)).LookupAsync(new(CollectorId: w.CollectorId, BusinessDate: Today, Amount: 1.234m))).Status);

        var other = await SeedAsync(canonical: true);
        await using var otherCtx = db.CreateContext(other.TenantId);
        var elsewhere = (await Reader(otherCtx, other, Admin(other)).LookupAsync(new(ClientOperationId: operation))).Value!;
        Assert.Equal("NoServerRecord", elsewhere.Verdict);                      // another municipality's collection is invisible
    }
}
