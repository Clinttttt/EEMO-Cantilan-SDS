using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Office Cash Turnover: cash the Head/Office collected directly (canonical Collections with no collector) and turned over with an
/// entered evidence reference. Independent of collector remittance; derived from the same Collections and corrections; no new receipt ledger.
/// </summary>
public sealed partial class EcfCollectionWorkflowTests
{
    private OfficeCashTurnoverWorkflow Turnover(AppDbContext ctx, Channels c, string role = "SuperAdmin", Guid? tenant = null,
        bool enabled = true, DateOnly? today = null)
    {
        var id = tenant ?? c.Seed.TenantId;
        return new(ctx, role == "Collector" ? new CollectorActor(c.Collector, id) : new TestActor(c.Seed.UserId, id, role), new FixedTenant(id),
            new OfficeCashTurnoverOptions(enabled), new MutableTestClock(today ?? PhilippineTime.Today));
    }

    private sealed record OfficePair(Guid FirstId, string FirstSrc, Guid SecondId, string SecondSrc);

    /// <summary>Two Office receipts of 600 each (the month is fully paid) and, optionally, one collector receipt on a second month.</summary>
    private async Task<(Channels C, OfficePair O)> ArrangeOfficeAsync()
    {
        var c = await ArrangeChannelsAsync();
        var first = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(first.IsSuccess, first.Error);
        var second = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(second.IsSuccess, second.Error);
        return (c, new OfficePair(first.Value!.CollectionId, first.Value.ReferenceCode, second.Value!.CollectionId, second.Value.ReferenceCode));
    }

    private static RecordOfficeCashTurnoverRequest Request(Guid operation, IReadOnlyList<Guid> collections, decimal amount,
        string? reference = "DEP-TEST-0001", string? by = "Head Cashier", string? remarks = null, DateOnly? date = null) =>
        new(operation, date ?? PhilippineTime.Today, PhilippineTime.Today, PhilippineTime.Today, collections, amount, reference, by, remarks);

    [SkippableFact]
    public async Task OfficeTurnover_review_lists_only_Office_origin_net_cash_and_the_Head_records_it_exactly()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);

        var review = (await Turnover(ctx, c).GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).Value!;
        Assert.Equal([o.FirstSrc, o.SecondSrc], review.Collections.Select(x => x.ReferenceCode).OrderBy(x => x).ToArray());
        Assert.Equal(1200m, review.ExpectedAmount);
        Assert.All(review.Collections, x => Assert.Equal((600m, 0m, 600m), (x.Amount, x.CorrectionEffect, x.NetAmount)));

        var recorded = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.SecondId], 1200m));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var row = recorded.Value!.Row;
        Assert.Equal((1200m, 1200m, 0m, OfficeCashTurnoverStatus.Recorded, 2), (row.ExpectedAmount, row.TurnedOverAmount, row.DifferenceAmount, row.Status, row.CollectionCount));
        Assert.Equal(("DEP-TEST-0001", "Head Cashier"), (row.Reference, row.TurnedOverBy));
        Assert.Equal(c.Seed.UserId.ToString("N"), row.RecordedByActorId);
        Assert.Empty((await Turnover(ctx, c).GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).Value!.Collections);   // nothing left uncovered
    }

    [SkippableFact]
    public async Task OfficeTurnover_is_Head_only_to_write_Admin_reads_and_collectors_tenants_and_the_default_gate_are_refused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var recorded = (await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m))).Value!;

        Assert.Equal(ResultStatus.Forbidden, (await Turnover(ctx, c, "Admin").RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m))).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Turnover(ctx, c, "Admin").VoidAsync(recorded.Row.Id, new("No authority"))).Status);
        Assert.True((await Turnover(ctx, c, "Admin").GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).IsSuccess);
        Assert.Single((await Turnover(ctx, c, "Admin").GetRegisterAsync(PhilippineTime.Today, PhilippineTime.Today)).Value!);
        Assert.Equal(recorded.Row.Id, (await Turnover(ctx, c, "Admin").GetDetailAsync(recorded.Row.Id)).Value!.Row.Id);

        foreach (var action in new Func<Task<ResultStatus>>[]
        {
            async () => (await Turnover(ctx, c, "Collector").GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).Status,
            async () => (await Turnover(ctx, c, "Collector").RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m))).Status,
            async () => (await Turnover(ctx, c, "Collector").VoidAsync(recorded.Row.Id, new("x"))).Status,
        }) Assert.Equal(ResultStatus.Forbidden, await action());

        var other = Guid.NewGuid();
        await using var foreign = db.CreateContext(other);
        Assert.Empty((await Turnover(foreign, c, tenant: other).GetRegisterAsync(PhilippineTime.Today, PhilippineTime.Today)).Value!);
        Assert.Equal(ResultStatus.NotFound, (await Turnover(foreign, c, tenant: other).GetDetailAsync(recorded.Row.Id)).Status);
        Assert.False((await Turnover(foreign, c, tenant: other).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m))).IsSuccess);
        Assert.False((await Turnover(foreign, c, tenant: other).VoidAsync(recorded.Row.Id, new("cross tenant"))).IsSuccess);

        Assert.False((await Turnover(ctx, c, enabled: false).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m))).IsSuccess);   // default-off
        Assert.Single(await ctx.OfficeCashTurnovers.AsNoTracking().ToArrayAsync());
    }

    [SkippableFact]
    public async Task OfficeTurnover_never_includes_collector_cash_and_collector_remittance_is_unaffected()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync();
        var officeReceipt = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(officeReceipt.IsSuccess, officeReceipt.Error);
        var collectorReceipt = await MobilePostAsync(c, 600, await VersionAsync(c));
        Assert.Equal(SyncResultStatus.Synced, collectorReceipt.Status);
        var o = new OfficePair(officeReceipt.Value!.CollectionId, officeReceipt.Value.ReferenceCode, Guid.Empty, "");

        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var today = PhilippineTime.Today;
        var before = (await Remit(ctx, c).GetReviewAsync(new(today, today))).Value!;
        Assert.Equal(600m, Assert.Single(before.Collectors).ExpectedAmount);

        var refused = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, collectorReceipt.CollectionId!.Value], 1200m));
        Assert.False(refused.IsSuccess);
        Assert.Empty(await ctx.OfficeCashTurnovers.AsNoTracking().ToArrayAsync());
        Assert.DoesNotContain(collectorReceipt.CollectionId!.Value, (await Turnover(ctx, c).GetReviewAsync(new(today, today))).Value!.Collections.Select(x => x.CollectionId));

        Assert.True((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m))).IsSuccess);
        var after = (await Remit(ctx, c).GetReviewAsync(new(today, today))).Value!;
        Assert.Equal((600m, 1), (Assert.Single(after.Collectors).ExpectedAmount, after.CollectionCount));         // collector figures untouched
        Assert.Empty(await ctx.CollectionRemittances.AsNoTracking().ToArrayAsync());
        var position = (await Remit(ctx, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((600m, 0m, 600m), (position.Collected, position.Remitted, position.Unremitted));
    }

    [SkippableFact]
    public async Task OfficeTurnover_covers_a_receipt_once_across_duplicates_retries_and_simultaneous_submissions()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var operation = Guid.NewGuid();

        var first = await Turnover(ctx, c).RecordAsync(Request(operation, [o.FirstId], 600m)); Assert.True(first.IsSuccess, first.Error);
        var replay = await Turnover(ctx, c).RecordAsync(Request(operation, [o.FirstId], 600m));                      // lost response, same intent
        Assert.True(replay.IsSuccess); Assert.Equal(first.Value!.Row.Id, replay.Value!.Row.Id);
        var changed = await Turnover(ctx, c).RecordAsync(Request(operation, [o.FirstId], 590m, remarks: "different intent"));
        Assert.False(changed.IsSuccess); Assert.Contains("OperationIntentConflict", changed.Error);
        var duplicate = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m));              // already covered
        Assert.False(duplicate.IsSuccess);
        Assert.Single(await ctx.OfficeCashTurnovers.AsNoTracking().ToArrayAsync());

        await using var left = db.CreateContext(c.Seed.TenantId); await using var right = db.CreateContext(c.Seed.TenantId);
        var results = await Task.WhenAll(
            Turnover(left, c).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m, "DEP-A")),
            Turnover(right, c).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m, "DEP-B")));
        Assert.Single(results, x => x.IsSuccess);
        await using var verify = db.CreateContext(c.Seed.TenantId);
        Assert.Equal(2, await verify.OfficeCashTurnovers.CountAsync());
        Assert.Equal(2, await verify.OfficeCashTurnoverCoverages.CountAsync(x => x.IsActive));
        Assert.Equal(2, await verify.OfficeCashTurnoverCoverages.Select(x => x.CollectionId).Distinct().CountAsync());
    }

    [SkippableFact]
    public async Task OfficeTurnover_uses_the_corrected_net_and_a_turned_over_receipt_cannot_be_reversed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var head = () => new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(PhilippineTime.Today),
            new OfficeCashTurnoverOptions(true));

        Assert.True((await head().ReverseAsync(new(Guid.NewGuid(), o.FirstId, "Wrong amount keyed"))).IsSuccess);          // a reversed receipt is no longer cash
        var review = (await Turnover(ctx, c).GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).Value!;
        Assert.Equal([o.SecondSrc], review.Collections.Select(x => x.ReferenceCode).ToArray());
        Assert.Equal(600m, review.ExpectedAmount);
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.SecondId], 1200m))).IsSuccess);   // net 0 cannot be turned over
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m))).IsSuccess);

        var recorded = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m)); Assert.True(recorded.IsSuccess, recorded.Error);
        var reversal = await head().ReverseAsync(new(Guid.NewGuid(), o.SecondId, "Too late"));
        Assert.Equal("TurnedOver", reversal.Error);
        var eligibility = (await head().ReviewAsync(o.SecondId)).Value!.Eligibility;
        Assert.False(eligibility.CanCorrect); Assert.Equal("TurnedOver", eligibility.BlockCode);
        Assert.Single(await ctx.CollectionCorrections.AsNoTracking().ToArrayAsync());                                   // only the first reversal exists
    }

    [SkippableFact]
    public async Task OfficeTurnover_void_keeps_history_releases_coverage_and_requires_a_reason()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var recorded = (await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.SecondId], 1200m))).Value!;

        Assert.False((await Turnover(ctx, c).VoidAsync(recorded.Row.Id, new("  "))).IsSuccess);
        Assert.False((await Turnover(ctx, c).VoidAsync(Guid.NewGuid(), new("Unknown"))).IsSuccess);
        var voided = await Turnover(ctx, c).VoidAsync(recorded.Row.Id, new("Slip was for another day"));
        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal((OfficeCashTurnoverStatus.Voided, "Slip was for another day"), (voided.Value!.Row.Status, voided.Value.Row.VoidReason));
        Assert.Equal(c.Seed.UserId.ToString("N"), voided.Value.Row.VoidedByActorId);
        Assert.False((await Turnover(ctx, c).VoidAsync(recorded.Row.Id, new("Again"))).IsSuccess);                      // already voided

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OfficeCashTurnovers.CountAsync());                                                     // history retained
        Assert.Equal((2, 0), (await ctx.OfficeCashTurnoverCoverages.CountAsync(), await ctx.OfficeCashTurnoverCoverages.CountAsync(x => x.IsActive)));
        Assert.Equal(2, (await Turnover(ctx, c).GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).Value!.Collections.Count);   // eligible again
        Assert.True((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.SecondId], 1200m, "DEP-TEST-0002"))).IsSuccess);
        Assert.Equal(2, (await Turnover(ctx, c).GetRegisterAsync(PhilippineTime.Today, PhilippineTime.Today)).Value!.Count);
        var head = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(PhilippineTime.Today), new OfficeCashTurnoverOptions(true));
        Assert.Equal("TurnedOver", (await head.ReverseAsync(new(Guid.NewGuid(), o.FirstId, "x"))).Error);                 // covered again by the new turnover
    }

    [SkippableTheory]
    [InlineData(500, true)]      // shortfall 100
    [InlineData(650, true)]      // excess 50
    [InlineData(600, false)]     // exact
    public async Task OfficeTurnover_keeps_shortfall_and_excess_visible_and_demands_an_explanation(double amount, bool differs)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var value = (decimal)amount;

        if (differs) Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], value))).IsSuccess);   // a difference needs remarks
        var recorded = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], value, remarks: differs ? "Counted again at the cashier" : null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var row = recorded.Value!.Row;
        Assert.Equal((600m, value, 600m - value), (row.ExpectedAmount, row.TurnedOverAmount, row.DifferenceAmount));
        Assert.Equal(differs, row.NeedsReview);
        ctx.ChangeTracker.Clear();
        Assert.Equal(600m, (await ctx.Collections.AsNoTracking().SingleAsync(x => x.Id == o.FirstId)).TotalAmount);          // never adjusted to hide it
    }

    [SkippableFact]
    public async Task OfficeTurnover_validates_the_evidence_and_dates_and_never_invents_any()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var today = PhilippineTime.Today;

        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m, reference: new string('x', 101)))).IsSuccess);   // too long; a missing reference is allowed
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m, by: null))).IsSuccess);
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 0m))).IsSuccess);
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600.005m))).IsSuccess);
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m, date: today.AddDays(1)))).IsSuccess);   // future
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [], 600m))).IsSuccess);
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.FirstId], 1200m))).IsSuccess);                // duplicate selection
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [Guid.NewGuid()], 600m))).IsSuccess);                       // unknown collection
        Assert.False((await Turnover(ctx, c).RecordAsync(Request(Guid.Empty, [o.FirstId], 600m))).IsSuccess);
        Assert.Empty(await ctx.OfficeCashTurnovers.AsNoTracking().ToArrayAsync());
        Assert.Empty(await ctx.AccountableDocuments.AsNoTracking().Where(x => x.CollectionId != null).ToArrayAsync());   // no physical OR/CT created
    }

    [SkippableFact]
    public async Task OfficeTurnover_leaves_the_Collections_SRCs_actors_and_Monthly_Income_adjustments_untouched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var snapshot = await ctx.Collections.AsNoTracking().OrderBy(x => x.ReferenceNumber)
            .Select(x => new { x.Id, x.ReferenceCode, x.TotalAmount, x.BusinessDate, x.RecordedAtUtc, x.ActorId, x.ActorName, x.CollectorId }).ToListAsync();
        var operations = await ctx.PostingOperations.CountAsync();

        var recorded = (await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.SecondId], 1200m))).Value!;
        Assert.True((await Turnover(ctx, c).VoidAsync(recorded.Row.Id, new("Test void"))).IsSuccess);
        Assert.True((await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId, o.SecondId], 1200m, "DEP-TEST-0003"))).IsSuccess);

        ctx.ChangeTracker.Clear();
        var after = await ctx.Collections.AsNoTracking().OrderBy(x => x.ReferenceNumber)
            .Select(x => new { x.Id, x.ReferenceCode, x.TotalAmount, x.BusinessDate, x.RecordedAtUtc, x.ActorId, x.ActorName, x.CollectorId }).ToListAsync();
        Assert.Equal(snapshot, after);
        Assert.Equal(operations, await ctx.PostingOperations.CountAsync());
        Assert.Empty(await ctx.CollectionCorrections.AsNoTracking().ToArrayAsync());

        var detail = (await Turnover(ctx, c).GetDetailAsync(recorded.Row.Id)).Value!;                                       // the first (voided) record keeps its evidence
        Assert.Equal(2, detail.Collections.Count);
        Assert.All(detail.Collections, x => { Assert.False(x.IsActive); Assert.Equal(snapshot.Single(s => s.Id == x.CollectionId).ActorId, x.OriginalActorId); });
        Assert.All(detail.Collections, x => Assert.Equal(snapshot.Single(s => s.Id == x.CollectionId).ActorName, x.OriginalActorName));

        // A Head Monthly Income adjustment is report-only: it changes neither the turnover expectation nor the Office-origin position.
        var tenant = c.Tenant;
        var adjusted = await new ReportGovernanceWorkflow(ctx, c.Head, tenant, new ProofClock(), new EEMOCantilanSDS.Infrastructure.Repositories.LegacyMonthlyIncomeReader(ctx))
            .AdjustAsync(new(Guid.NewGuid(), "RENT_NCC", PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1200, 1254, "Report-only revision"));
        Assert.True(adjusted.IsSuccess, adjusted.Error);
        var register = (await Turnover(ctx, c).GetRegisterAsync(PhilippineTime.Today, PhilippineTime.Today)).Value!;
        Assert.Equal([1200m, 1200m], register.OrderBy(x => x.RecordedAtUtc).Select(x => x.ExpectedAmount).ToArray());
        Assert.Equal(1200m, (await Remit(ctx, c).GetPositionAsync(PhilippineTime.Today, PhilippineTime.Today)).Value!.OfficeOriginCollected);
    }

    [SkippableFact]
    public async Task OfficeTurnover_position_separates_collected_covered_and_pending_and_follows_reversal_and_void()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        var today = PhilippineTime.Today;
        await using var ctx = db.CreateContext(c.Seed.TenantId);

        var start = (await Turnover(ctx, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((1200m, 0m, 1200m, 2, 2), (start.Collected, start.Covered, start.Pending, start.CollectionCount, start.PendingCount));

        var recorded = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 500m, remarks: "Counted again at the cashier")); Assert.True(recorded.IsSuccess, recorded.Error);
        var covered = (await Turnover(ctx, c, "Admin").GetPositionAsync(today, today)).Value!;                             // Admin may read
        Assert.Equal((1200m, 600m, 600m, 500m, 100m, 1), (covered.Collected, covered.Covered, covered.Pending, covered.TurnedOverAmount, covered.NetDifference, covered.NeedsReviewCount));

        var head = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(today), new OfficeCashTurnoverOptions(true));
        Assert.True((await head.ReverseAsync(new(Guid.NewGuid(), o.SecondId, "Wrong amount keyed"))).IsSuccess);          // the uncovered one
        var reversed = (await Turnover(ctx, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((600m, 600m, 0m, 1, 0), (reversed.Collected, reversed.Covered, reversed.Pending, reversed.CollectionCount, reversed.PendingCount));

        Assert.True((await Turnover(ctx, c).VoidAsync(recorded.Value!.Row.Id, new("Slip was for another day"))).IsSuccess);
        var voided = (await Turnover(ctx, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((600m, 0m, 600m, 0m, 0m, 0), (voided.Collected, voided.Covered, voided.Pending, voided.TurnedOverAmount, voided.NetDifference, voided.NeedsReviewCount));

        Assert.Equal(ResultStatus.Forbidden, (await Turnover(ctx, c, "Collector").GetPositionAsync(today, today)).Status);
        var yesterday = (await Turnover(ctx, c).GetPositionAsync(today.AddDays(-1), today.AddDays(-1))).Value!;               // date boundary
        Assert.Equal((0m, 0m, 0m, 0), (yesterday.Collected, yesterday.Covered, yesterday.Pending, yesterday.CollectionCount));
    }

    [SkippableFact]
    public async Task OfficeTurnover_availability_and_the_tenant_list_keep_other_municipalities_out()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        OfficeCashTurnoverWorkflow With(OfficeCashTurnoverOptions options, string role = "SuperAdmin") =>
            new(ctx, new TestActor(c.Seed.UserId, c.Seed.TenantId, role), c.Tenant, options, new MutableTestClock(PhilippineTime.Today));

        Assert.True((await With(new OfficeCashTurnoverOptions(true, new HashSet<Guid> { c.Seed.TenantId })).IsAvailableAsync()).Value);
        Assert.True((await With(new OfficeCashTurnoverOptions(true, new HashSet<Guid> { c.Seed.TenantId }), "Admin").IsAvailableAsync()).Value);
        Assert.False((await With(new OfficeCashTurnoverOptions(true, new HashSet<Guid> { Guid.NewGuid() })).IsAvailableAsync()).Value);   // another tenant only
        Assert.False((await With(new OfficeCashTurnoverOptions()).IsAvailableAsync()).Value);                                           // default off
        Assert.Equal(ResultStatus.Forbidden, (await With(new OfficeCashTurnoverOptions(true), "Collector").IsAvailableAsync()).Status);

        var elsewhere = With(new OfficeCashTurnoverOptions(true, new HashSet<Guid> { Guid.NewGuid() }));
        Assert.False((await elsewhere.RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m))).IsSuccess);
        Assert.False((await elsewhere.GetReviewAsync(new(PhilippineTime.Today, PhilippineTime.Today))).IsSuccess);
        Assert.True((await With(new OfficeCashTurnoverOptions(true, new HashSet<Guid> { c.Seed.TenantId })).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m))).IsSuccess);
    }
}
