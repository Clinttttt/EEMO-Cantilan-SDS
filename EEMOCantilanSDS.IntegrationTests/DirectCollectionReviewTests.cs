using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The Head's direct-collection review reads the canonical Collections and adds only an append-only review event. Confirming, flagging or
/// resolving never creates a Collection or an SRC, never changes an amount, and never removes cash from accountability. It is Head-only,
/// tenant-isolated, idempotent per operation id, and a possible duplicate is flagged and left out of bulk confirmation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DirectCollectionReviewTests(PostgresFixture db)
{
    private static DateOnly Today => PhilippineTime.Today;

    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "field-collector" : "head-reviewer";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "review-x";
        public Guid? MunicipalityId => tenantId;
    }
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid municipalityId) { } }
    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private sealed record World(Guid TenantId, Guid Head, Guid Collector, Guid NpmStall);

    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"rv-{Guid.NewGuid():N}"[..12], "Review X", "Province", MunicipalityStatus.Active,
            tenantCode: $"revx-{Guid.NewGuid():N}"[..24].ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Public Market", "NPM", archetype: BillingArchetype.DailyStall, municipalityId: tenant.Id);
        ctx.Add(npm);
        var stall = Stall.Create(npm.Id, "V-1", 900m, ApplicableFees.BaseRental, null, municipalityId: tenant.Id);
        ctx.Add(stall);
        ctx.Add(Contract.Create(stall.Id, "Vic Vendor", "Vic Vendor", new DateOnly(Today.Year, Today.Month, 1).AddYears(-1), 5, 900m, createdBy: "test"));
        var wcf = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenant.Id);
        ctx.AddRange(wcf, RevenueClassificationPolicy.Create(wcf.Id, new DateOnly(2000, 1, 1), "Water Consumption Fees", RevenueInstrumentType.CashTicket, tenant.Id));
        var collector = CollectorUser.Create("Pat", "C-Pat", "pat" + Guid.NewGuid().ToString("N")[..6], null, null, new HashedPassword("h"), tenant.Id);
        collector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.Wcf, "head"));
        ctx.Add(collector);
        ctx.Add(CollectorFacilityAssignment.Create(collector.Id, npm.Id, npm.Code));
        await ctx.SaveChangesAsync();
        var head = Guid.NewGuid();
        Assert.True((await new WcfMobileCollectionWorkflow(ctx, new Actor(head, tenant.Id, "Admin"), new FixedTenant(tenant.Id), new Clock()).EnableAsync()).IsSuccess);
        return new(tenant.Id, head, collector.Id, stall.Id);
    }

    private static DirectCollectionReviewWorkflow Review(AppDbContext ctx, World w, string role = "Admin", Guid? tenant = null) =>
        new(ctx, new Actor(role == "Collector" ? w.Collector : w.Head, tenant ?? w.TenantId, role), new FixedTenant(tenant ?? w.TenantId), new Clock());

    private static async Task<Guid> RecordDirectAsync(AppDbContext ctx, World w, decimal amount)
    {
        // A further direct payment for the same source and period carries the source version the earlier one advanced.
        var version = await ctx.UtilityBills.Where(b => b.StallId == w.NpmStall && b.BillingYear == Today.Year && b.BillingMonth == Today.Month)
            .Select(b => (long?)b.WaterSourceVersion).SingleOrDefaultAsync() ?? 0L;
        var posted = await new WcfCollectionWorkflow(ctx, new Actor(w.Collector, w.TenantId, "Collector"), new FixedTenant(w.TenantId), clock: new Clock())
            .PostMobileAsync(new(1, Guid.NewGuid(), Today, Guid.Empty, amount, version, StallId: w.NpmStall, BillingYear: Today.Year, BillingMonth: Today.Month));
        Assert.True(posted.IsSuccess, posted.Error);
        return posted.Value!.CollectionId;
    }

    private static DirectCollectionReviewRequest Pending(string state = "Pending") => new(state, Today.AddDays(-1), Today);

    [SkippableFact]
    public async Task ADirectCollectionAppearsOnce_AndConfirmingItCreatesNoCollection_NorChangesMoney_AndARetryIsIdempotent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var collectionId = await RecordDirectAsync(ctx, w, 10m);
        var before = (await ctx.Collections.CountAsync(), await ctx.Collections.SumAsync(c => c.TotalAmount));

        var page = (await Review(ctx, w).GetAsync(Pending())).Value!;
        var row = Assert.Single(page.Rows);
        Assert.Equal((collectionId, 10m, "WCF", "Awaiting review", "Posted", true), (row.CollectionId, row.Amount, row.Utility, row.ReviewState, row.Disposition, row.CanConfirm));
        Assert.StartsWith("SRC-", row.ReferenceCode);
        Assert.Equal(("Direct", "field-collector"), (row.Mode, row.RecordedBy));
        Assert.Equal(1, page.PendingCount);
        Assert.Contains("Stall V-1", row.SourceContext);

        var op = Guid.NewGuid();
        var confirmed = await Review(ctx, w).ReviewAsync(new(op, collectionId, "Confirmed"));
        Assert.Equal(("Confirmed", "head-reviewer"), (confirmed.Value!.ReviewState, confirmed.Value.ReviewedBy));
        Assert.True((await Review(ctx, w).ReviewAsync(new(op, collectionId, "Confirmed"))).IsSuccess);                       // the same retry answers, adds nothing
        Assert.Equal(1, await ctx.DirectCollectionReviewEvents.CountAsync());

        // Money is untouched: same Collections, same total, same SRC. Review is not a financial event.
        Assert.Equal(before, (await ctx.Collections.CountAsync(), await ctx.Collections.SumAsync(c => c.TotalAmount)));
        Assert.Empty((await Review(ctx, w).GetAsync(Pending())).Value!.Rows);
        Assert.Equal(0, (await Review(ctx, w).GetAsync(Pending())).Value!.PendingCount);
        Assert.Equal(collectionId, Assert.Single((await Review(ctx, w).GetAsync(Pending("Reviewed"))).Value!.Rows).CollectionId);
        Assert.Single((await Review(ctx, w).GetAsync(Pending("All"))).Value!.Rows);                                          // still a collected, listed Collection
        Assert.False((await Review(ctx, w).ReviewAsync(new(op, collectionId, "NeedsCorrection", "different"))).IsSuccess);   // an operation id cannot be reused for another review
    }

    [SkippableFact]
    public async Task NeedsCorrectionRequiresANote_AndOnlyAFlaggedCollectionCanBeResolved_WithoutTouchingTheMoney()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await RecordDirectAsync(ctx, w, 25m);

        Assert.False((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection"))).IsSuccess);             // a flag says why
        Assert.False((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Resolved"))).IsSuccess);                    // nothing to resolve yet
        var flagged = (await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection", "Amount looks short"))).Value!;
        Assert.Equal(("Needs correction", "Amount looks short"), (flagged.ReviewState, flagged.ReviewNote));
        Assert.Equal(id, Assert.Single((await Review(ctx, w).GetAsync(Pending("NeedsCorrection"))).Value!.Rows).CollectionId);
        Assert.Empty((await Review(ctx, w).GetAsync(Pending("Pending"))).Value!.Rows);

        Assert.Equal("Resolved", (await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Resolved", "Collector re-checked"))).Value!.ReviewState);
        Assert.Equal(25m, (await ctx.Collections.SingleAsync(c => c.Id == id)).TotalAmount);                                  // never changed
        Assert.Equal(2, await ctx.DirectCollectionReviewEvents.CountAsync());                                                // every step is kept as history
    }

    [SkippableFact]
    public async Task ATwinCollectionIsFlaggedAsAPossibleDuplicate_AndBulkConfirmationSkipsItButConfirmsTheRest()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var first = await RecordDirectAsync(ctx, w, 10m);
        var twin = await RecordDirectAsync(ctx, w, 10m);                                       // the same source, part, day and amount again

        var rows = (await Review(ctx, w).GetAsync(Pending())).Value!.Rows;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.PossibleDuplicate && !r.CanConfirm));

        var result = (await Review(ctx, w).BulkConfirmAsync(new(Guid.NewGuid(), [first, twin]))).Value!;
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains("duplicate", result.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ctx.DirectCollectionReviewEvents.CountAsync());

        // A deliberate single review of each is still possible: the Head decides, the system never blocks genuine repeat payments.
        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), twin, "Confirmed"))).IsSuccess);
        Assert.Equal(2, await ctx.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task OnlyAHeadOfThisTenantMayReview_AndAnotherTenantsCollectionIsNotFound()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await RecordDirectAsync(ctx, w, 10m);

        Assert.False((await Review(ctx, w, "Collector").GetAsync(Pending())).IsSuccess);                                      // a Collector cannot review
        Assert.False((await Review(ctx, w, "Collector").ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);
        await using var otherCtx = db.CreateContext(other.TenantId);
        Assert.Empty((await Review(otherCtx, other).GetAsync(Pending("All"))).Value!.Rows);                                  // another tenant sees nothing of this one
        Assert.False((await Review(otherCtx, other).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"))).IsSuccess);
        Assert.Equal(0, await ctx.DirectCollectionReviewEvents.CountAsync());
    }
}
