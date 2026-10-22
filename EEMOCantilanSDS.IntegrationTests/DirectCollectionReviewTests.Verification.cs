using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Verification of Collector direct collections: Recorded = Verified + Pending, always net of valid corrections, with the cash counted as
/// received whether or not it is verified. Verification is only the latest append-only review event.
/// </summary>
public sealed partial class DirectCollectionReviewTests
{
    private static async Task ReverseAsync(AppDbContext ctx, Guid collectionId)
    {
        var c = await ctx.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync(x => x.Id == collectionId);
        ctx.CollectionCorrections.Add(CollectionCorrection.Record(c.MunicipalityId, c.Id, null, null, null, CollectionCorrectionType.Reversal, Today,
            DateTime.UtcNow, -c.TotalAmount, "Test reversal", "actor", "tester",
            c.Lines.Select(l => new CollectionCorrectionLineDraft(l.Id, -l.Amount,
                l.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -a.Amount)).ToArray()))));
        await ctx.SaveChangesAsync();
    }

    private static async Task<DirectCollectionVerificationPositionDto> PositionAsync(AppDbContext ctx, World w, string role = "SuperAdmin") =>
        (await Review(ctx, w, role).GetPositionAsync(Today, Today)).Value!;

    private static async Task<string?> LabelAsync(AppDbContext ctx, World w, Guid id) =>
        (await new CollectionActivityReader(ctx).GetAsync(w.TenantId, Today, Today)).Single(x => x.CollectionId == id).Verification;

    private static async Task<(decimal Collected, decimal Unremitted, decimal Expected)> CollectorCashAsync(AppDbContext ctx, World w)
    {
        var remit = new RemittanceWorkflow(ctx, new Actor(w.Head, w.TenantId, "SuperAdmin"), new FixedTenant(w.TenantId));
        var position = (await remit.GetPositionAsync(Today, Today)).Value!;
        var review = (await remit.GetReviewAsync(new(Today, Today))).Value!;
        return (position.Collected, position.Unremitted, review.Total);
    }

    [SkippableFact]
    public async Task Pending_to_Confirmed_changes_only_the_verification_and_never_the_cash_the_SRC_or_the_collector_figures()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await RecordDirectAsync(ctx, w, 10m);
        var original = await ctx.Collections.AsNoTracking().SingleAsync(c => c.Id == id);
        var cashBefore = await CollectorCashAsync(ctx, w);
        var reports = new GetOfficialMonthlyIncomeQueryHandler(ctx, new LegacyMonthlyIncomeReader(ctx), new Actor(w.Head, w.TenantId, "SuperAdmin"), new FixedTenant(w.TenantId), new Clock());
        var incomeBefore = (await reports.Handle(new(Today.Year, Today.Month), default)).Value!.GrandTotal.SystemAmount;

        var before = await PositionAsync(ctx, w);
        Assert.Equal((10m, 0m, 10m, 10m, 1, 0, 1), (before.Recorded, before.Verified, before.PendingVerification, before.AwaitingReview, before.CollectionCount, before.VerifiedCount, before.PendingCount));
        Assert.Equal("Awaiting verification", await LabelAsync(ctx, w, id));

        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);
        var after = await PositionAsync(ctx, w);
        Assert.Equal((10m, 10m, 0m, 0m, 1, 1, 0), (after.Recorded, after.Verified, after.PendingVerification, after.AwaitingReview, after.CollectionCount, after.VerifiedCount, after.PendingCount));
        Assert.Equal(after.Recorded, after.Verified + after.PendingVerification);
        Assert.Equal("Verified", await LabelAsync(ctx, w, id));

        // Confirmation is evidence only: same Collection and SRC, same cash everywhere, no other financial record created.
        ctx.ChangeTracker.Clear();
        var kept = await ctx.Collections.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal((original.ReferenceCode, original.TotalAmount, original.BusinessDate, original.RecordedAtUtc), (kept.ReferenceCode, kept.TotalAmount, kept.BusinessDate, kept.RecordedAtUtc));
        Assert.Equal(1, await ctx.Collections.CountAsync());
        Assert.Empty(await ctx.CollectionRemittances.AsNoTracking().ToArrayAsync());
        Assert.Equal(cashBefore, await CollectorCashAsync(ctx, w));
        Assert.Equal(incomeBefore, (await reports.Handle(new(Today.Year, Today.Month), default)).Value!.GrandTotal.SystemAmount);
        Assert.Equal(10m, (await new GetRevenueSourcePerformanceQueryHandler(reports, ctx, new FixedTenant(w.TenantId)).Handle(new(Today.Year, Today.Month), default)).Value!.TotalCollected);
    }

    [SkippableFact]
    public async Task A_flagged_collection_stays_out_of_verified_until_resolved_and_then_explicitly_confirmed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await RecordDirectAsync(ctx, w, 25m);

        Assert.True((await Review(ctx, w, "Admin").ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection", "Amount looks short"))).IsSuccess);   // Admin may flag
        var flagged = await PositionAsync(ctx, w);
        Assert.Equal((25m, 0m, 25m, 25m, 0m), (flagged.Recorded, flagged.Verified, flagged.PendingVerification, flagged.NeedsCorrection, flagged.AwaitingReview));
        Assert.Equal("Needs correction", await LabelAsync(ctx, w, id));

        var confirmFlagged = await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"));
        Assert.False(confirmFlagged.IsSuccess); Assert.Contains("Resolve it first", confirmFlagged.Error);
        Assert.False((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection", "again"))).IsSuccess);             // already flagged

        Assert.Equal("Resolved", (await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Resolved", "Collector re-checked the slip"))).Value!.ReviewState);
        var resolved = await PositionAsync(ctx, w);
        Assert.Equal((25m, 0m, 25m, 25m), (resolved.Recorded, resolved.Verified, resolved.PendingVerification, resolved.ResolvedAwaitingConfirmation));   // resolving is not verifying
        Assert.Equal("Awaiting confirmation", await LabelAsync(ctx, w, id));

        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);
        Assert.Equal((25m, 25m, 0m), ((await PositionAsync(ctx, w)).Recorded, (await PositionAsync(ctx, w)).Verified, (await PositionAsync(ctx, w)).PendingVerification));
        Assert.False((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);                              // a stale second confirm
        Assert.Equal(3, await ctx.DirectCollectionReviewEvents.CountAsync());                                                          // flag, resolve, confirm: every step kept as history
        Assert.Equal(25m, (await ctx.Collections.AsNoTracking().SingleAsync(c => c.Id == id)).TotalAmount);
    }

    [SkippableFact]
    public async Task Only_the_Head_verifies_Admin_reads_and_flags_and_collectors_and_other_tenants_have_nothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await RecordDirectAsync(ctx, w, 10m);
        var second = await RecordDirectAsync(ctx, w, 11m);

        Assert.True((await Review(ctx, w, "Admin").GetAsync(Pending())).IsSuccess);                                                    // Admin reads...
        Assert.Equal(21m, (await PositionAsync(ctx, w, "Admin")).Recorded);
        Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, "Admin").ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).Status);  // ...but never verifies
        Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, "Admin").ReviewAsync(new(Guid.NewGuid(), id, "Resolved"))).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, "Admin").BulkConfirmAsync(new(Guid.NewGuid(), [id, second]))).Status);
        Assert.Equal(0, await ctx.DirectCollectionReviewEvents.CountAsync());
        Assert.True((await Review(ctx, w, "Admin").ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection", "Please re-check the amount"))).IsSuccess);

        foreach (var role in new[] { "Collector", "Payor" })
        {
            Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, role).GetAsync(Pending())).Status);
            Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, role).GetPositionAsync(Today, Today)).Status);
            Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, role).ReviewAsync(new(Guid.NewGuid(), second, "Confirmed"))).Status);
            Assert.Equal(ResultStatus.Forbidden, (await Review(ctx, w, role).BulkConfirmAsync(new(Guid.NewGuid(), [second]))).Status);
        }
        await using var otherCtx = db.CreateContext(other.TenantId);
        Assert.Equal(0m, (await PositionAsync(otherCtx, other)).Recorded);                                                             // another tenant sees none of it
        Assert.Equal(ResultStatus.NotFound, (await Review(otherCtx, other).ReviewAsync(new(Guid.NewGuid(), second, "Confirmed"))).Status);
        Assert.Equal(1, await ctx.DirectCollectionReviewEvents.CountAsync());                                                          // only the Admin flag exists
    }

    [SkippableFact]
    public async Task Single_and_bulk_confirmation_refuse_the_same_cases_for_the_same_reasons()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var ok = await RecordDirectAsync(ctx, w, 10m);
        var reversed = await RecordDirectAsync(ctx, w, 11m);
        var flagged = await RecordDirectAsync(ctx, w, 12m);
        var confirmed = await RecordDirectAsync(ctx, w, 13m);
        await ReverseAsync(ctx, reversed);
        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), flagged, "NeedsCorrection", "Wrong source"))).IsSuccess);
        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), confirmed, "Confirmed"))).IsSuccess);

        // The rows themselves say which are confirmable, from the same rules.
        var rows = (await Review(ctx, w).GetAsync(Pending("All"))).Value!.Rows.ToDictionary(r => r.CollectionId);
        Assert.Equal((true, null), (rows[ok].CanConfirm, rows[ok].ConfirmBlock));
        Assert.Equal(("Reversed", false), (rows[reversed].ConfirmBlock, rows[reversed].CanConfirm));
        Assert.Equal("CorrectionUnresolved", rows[flagged].ConfirmBlock);
        Assert.Equal("AlreadyConfirmed", rows[confirmed].ConfirmBlock);
        Assert.Equal(0m, rows[reversed].NetAmount);

        var singleErrors = new Dictionary<Guid, string?>();
        foreach (var id in new[] { reversed, flagged, confirmed })
            singleErrors[id] = (await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).Error;
        var bulk = (await Review(ctx, w).BulkConfirmAsync(new(Guid.NewGuid(), [ok, reversed, flagged, confirmed]))).Value!;

        Assert.Equal(1, bulk.Confirmed);
        Assert.Equal(3, bulk.Skipped.Count);
        foreach (var skip in bulk.Skipped) Assert.Equal(singleErrors[skip.CollectionId], skip.Reason);                                   // identical wording: one rule set
        Assert.Contains((await Review(ctx, w).GetAsync(Pending("Reviewed"))).Value!.Rows, r => r.CollectionId == ok);
        // Repeating the identical bulk request adds nothing.
        var eventsAfter = await ctx.DirectCollectionReviewEvents.CountAsync();
        var repeat = await Review(ctx, w).BulkConfirmAsync(new(Guid.NewGuid(), [ok]));
        Assert.Equal(0, repeat.Value!.Confirmed);                                                                                       // already confirmed: skipped now
        Assert.Equal(eventsAfter, await ctx.DirectCollectionReviewEvents.CountAsync());
    }

    [SkippableFact]
    public async Task Two_reviewers_acting_at_once_leave_one_confirmation_and_a_clear_conflict()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var seedCtx = db.CreateContext(w.TenantId);
        var id = await RecordDirectAsync(seedCtx, w, 10m);

        await using var left = db.CreateContext(w.TenantId); await using var right = db.CreateContext(w.TenantId);
        var results = await Task.WhenAll(Review(left, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed")), Review(right, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed")));

        Assert.Equal(1, results.Count(x => x.IsSuccess));
        Assert.Equal(ResultStatus.Conflict, results.Single(x => !x.IsSuccess).Status);
        Assert.Equal(1, await seedCtx.DirectCollectionReviewEvents.CountAsync());
        Assert.Equal(10m, (await PositionAsync(seedCtx, w)).Verified);                                                                // counted once, not twice
    }

    [SkippableFact]
    public async Task An_offline_replay_is_one_Collection_so_one_verification_and_a_reversal_is_never_positive_verified_revenue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        var operation = Guid.NewGuid();
        var flow = new WcfCollectionWorkflow(ctx, new Actor(w.Collector, w.TenantId, "Collector"), new FixedTenant(w.TenantId), clock: new Clock());
        var first = await flow.PostMobileAsync(new(1, operation, Today, Guid.Empty, 10m, 0, StallId: w.NpmStall, BillingYear: Today.Year, BillingMonth: Today.Month));
        var replay = await flow.PostMobileAsync(new(1, operation, Today, Guid.Empty, 10m, 0, StallId: w.NpmStall, BillingYear: Today.Year, BillingMonth: Today.Month));
        Assert.True(first.IsSuccess && replay.IsSuccess, first.Error ?? replay.Error);
        Assert.Equal(first.Value!.CollectionId, replay.Value!.CollectionId);                                                           // exact-once synchronization
        var id = first.Value.CollectionId;
        Assert.Equal(1, await ctx.Collections.CountAsync());

        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);
        Assert.Equal((10m, 10m), ((await PositionAsync(ctx, w)).Recorded, (await PositionAsync(ctx, w)).Verified));

        // A later reversal turns the verified cash to nothing: never positive net, never pending, and nothing further can be confirmed or flagged.
        var cashBefore = await CollectorCashAsync(ctx, w);
        await ReverseAsync(ctx, id);
        var reversed = await PositionAsync(ctx, w);
        Assert.Equal((0m, 0m, 0m, 0), (reversed.Recorded, reversed.Verified, reversed.PendingVerification, reversed.CollectionCount));
        Assert.Equal("Reversed", await LabelAsync(ctx, w, id));
        Assert.False((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);
        Assert.False((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection", "flag a reversed one"))).IsSuccess);
        Assert.NotEqual(cashBefore, await CollectorCashAsync(ctx, w));                                                                 // the collector figures follow the correction, not the review
        Assert.Equal(0m, (await CollectorCashAsync(ctx, w)).Collected);
        Assert.Equal(1, await ctx.Collections.CountAsync()); Assert.Equal(1, await ctx.DirectCollectionReviewEvents.CountAsync());   // the SRC and the history stay
    }
}
