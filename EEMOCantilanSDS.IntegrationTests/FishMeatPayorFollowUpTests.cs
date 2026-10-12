using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A Fish/Meat Vendor Fee recorded against a payer name only stays a posted, counted Collection. The Head can later identify the payor by
/// registering a vendor or linking an existing registration; that adds one append-only link and never touches the Collection, its SRC, payer
/// snapshot or amount. A matching name is only ever a suggestion.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FishMeatPayorFollowUpTests(PostgresFixture db)
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
        public string? MunicipalityCode => "follow-x";
        public Guid? MunicipalityId => tenantId;
    }
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid municipalityId) { } }
    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private sealed record World(Guid TenantId, Guid Head, Guid Collector);

    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"fm-{Guid.NewGuid():N}"[..12], "Follow X", "Province", MunicipalityStatus.Active,
            tenantCode: $"fmx-{Guid.NewGuid():N}"[..24].ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(tenant.Id);
        var fee = RevenueClassification.Create(RevenueClassificationCodes.FishMeatVendorFee, tenant.Id);
        ctx.AddRange(fee, RevenueClassificationPolicy.Create(fee.Id, new DateOnly(2000, 1, 1), "Fish / Meat Vendor Fee", RevenueInstrumentType.OfficialReceipt, tenant.Id));
        var collector = CollectorUser.Create("Pat", "C-Pat", "pat" + Guid.NewGuid().ToString("N")[..6], null, null, new HashedPassword("h"), tenant.Id);
        collector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.FishMeatVendorFee, "head"));
        ctx.Add(collector);
        await ctx.SaveChangesAsync();
        return new(tenant.Id, Guid.NewGuid(), collector.Id);
    }

    private static OfficeCollectionWorkflow Office(AppDbContext ctx, World w, string role) =>
        new(ctx, new Actor(role == "Collector" ? w.Collector : w.Head, w.TenantId, role), new FixedTenant(w.TenantId), new Clock(), new FeeRateResolver(ctx));

    private static FishMeatPayorFollowUpWorkflow FollowUp(AppDbContext ctx, World w, string role = "SuperAdmin", Guid? tenant = null) =>
        new(ctx, new Actor(w.Head, tenant ?? w.TenantId, role), new FixedTenant(tenant ?? w.TenantId), Office(ctx, w, role));

    private static DirectCollectionReviewWorkflow Review(AppDbContext ctx, World w, string role = "Admin") =>
        new(ctx, new Actor(w.Head, w.TenantId, role), new FixedTenant(w.TenantId), new Clock());

    private static async Task<Guid> PostPayerOnlyAsync(AppDbContext ctx, World w, string payer, decimal amount = 43m)
    {
        var result = await Office(ctx, w, "Collector").PostAsync(new(Guid.NewGuid(), Today, amount,
            new SourceNativeChargeIntent(CollectorOperationCodes.FishMeatVendorFee), payer));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.CollectionId;
    }

    [SkippableFact]
    public async Task APayerOnlyVendorFee_NeedsRegistration_StaysInReview_AndIsKeptOutOfBulkConfirm()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await PostPayerOnlyAsync(ctx, w, "Giorgia Macapagal");

        var list = (await FollowUp(ctx, w, "Admin").ListAsync(null, 0, 25)).Value!;
        var row = Assert.Single(list.Rows);
        Assert.Equal(id, row.CollectionId);
        Assert.Equal("Giorgia Macapagal", row.Payor);
        Assert.Equal(43m, row.Amount);

        var review = await Review(ctx, w).GetAsync(new("NeedsRegistration", Today.AddDays(-1), Today));
        var reviewRow = Assert.Single(review.Value!.Rows);
        Assert.Equal("Needs registration", reviewRow.Registration);
        Assert.Equal("Posted", reviewRow.Disposition);
        Assert.False(reviewRow.CanConfirm);
        Assert.Equal(1, review.Value.NeedsRegistrationCount);
        // The money is financially reviewable on its own and still counted; only bulk confirmation of an unidentified payor is refused.
        Assert.Single((await Review(ctx, w).GetAsync(new("Pending", Today.AddDays(-1), Today))).Value!.Rows);
        var bulk = (await Review(ctx, w).BulkConfirmAsync(new(Guid.NewGuid(), [id]))).Value!;
        Assert.Equal(0, bulk.Confirmed);
        Assert.Contains("register or link", Assert.Single(bulk.Skipped).Reason);
        var confirmed = await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "Confirmed"));
        Assert.True(confirmed.IsSuccess);
        Assert.Equal("Needs registration", confirmed.Value!.Registration);   // financial review never resolves identity
    }

    [SkippableFact]
    public async Task ARegisteredVendorFee_NeverAppearsInTheFollowUp()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var vendor = (await Office(ctx, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Registered Rita"))).Value!;
        var posted = await Office(ctx, w, "Collector").PostAsync(new(Guid.NewGuid(), Today, 50m,
            new SourceNativeChargeIntent(CollectorOperationCodes.FishMeatVendorFee, vendor.Id), null));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Empty((await FollowUp(ctx, w).ListAsync(null, 0, 25)).Value!.Rows);
    }

    [SkippableFact]
    public async Task RegisteringThePayor_LinksOnce_AndLeavesTheCollectionUntouched_AndARetryIsIdempotent()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await PostPayerOnlyAsync(ctx, w, "Giorgia Macapagal");
        var before = await ctx.Collections.AsNoTracking().SingleAsync(c => c.Id == id);
        var collectionsBefore = await ctx.Collections.CountAsync(c => c.MunicipalityId == w.TenantId);

        var op = Guid.NewGuid();
        var resolved = await FollowUp(ctx, w).ResolveAsync(new(op, id, Register: new(FishMeatVendorType.Fish, "Giorgia Macapagal", "Giorgia Fish Stand", "Stall 4")));
        Assert.True(resolved.IsSuccess, resolved.Error);
        Assert.True(resolved.Value!.Registered);
        Assert.Equal(Today.Year, resolved.Value.Registration.TaxYear);

        // The registered-vendor registry now carries them; the follow-up no longer does.
        Assert.Contains((await Office(ctx, w, "SuperAdmin").ManageRegistrationsAsync(Today.Year, Today.Month)).Value!.Rows, r => r.Registration.DisplayName == "Giorgia Macapagal");
        Assert.Empty((await FollowUp(ctx, w).ListAsync(null, 0, 25)).Value!.Rows);
        Assert.Empty((await Review(ctx, w).GetAsync(new("NeedsRegistration", Today.AddDays(-1), Today))).Value!.Rows);
        Assert.StartsWith("Registered: Giorgia Macapagal", (await Review(ctx, w).GetAsync(new("All", Today.AddDays(-1), Today))).Value!.Rows.Single().Registration);

        // The posted Collection is exactly as the Collector recorded it: same SRC, amount, payer snapshot; no second Collection or SRC.
        await using var after = db.CreateContext(w.TenantId);
        var kept = await after.Collections.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(before.ReferenceCode, kept.ReferenceCode);
        Assert.Equal(before.TotalAmount, kept.TotalAmount);
        Assert.Equal("Giorgia Macapagal", kept.PayerName);
        Assert.Equal(collectionsBefore, await after.Collections.CountAsync(c => c.MunicipalityId == w.TenantId));
        Assert.Equal(1, await after.FishMeatDirectCollectionLinks.CountAsync(l => l.CollectionId == id));

        // A retry of the same operation answers with what it recorded; a different operation cannot link it twice.
        var replay = await FollowUp(after, w).ResolveAsync(new(op, id, Register: new(FishMeatVendorType.Fish, "Giorgia Macapagal", "Giorgia Fish Stand", "Stall 4")));
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(resolved.Value.Registration.Id, replay.Value!.Registration.Id);
        var again = await FollowUp(after, w).ResolveAsync(new(Guid.NewGuid(), id, VendorRegistrationId: resolved.Value.Registration.Id));
        Assert.Equal(ResultStatus.Conflict, again.Status);
        Assert.Equal(1, await after.FishMeatDirectCollectionLinks.CountAsync(l => l.CollectionId == id));
    }

    [SkippableFact]
    public async Task ASimilarNameIsOnlyASuggestion_AndLinkingNeedsAnExplicitChoiceOfTheSameTaxYear()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var thisYear = (await Office(ctx, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Mark Lorgadar"))).Value!;
        var lastYear = (await Office(ctx, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), Today.Year - 1, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Mark Lorgadar"))).Value!;
        var id = await PostPayerOnlyAsync(ctx, w, "Mark Lorgadar");

        // A name match changes nothing by itself.
        var detail = (await FollowUp(ctx, w).DetailAsync(id)).Value!;
        Assert.Equal(thisYear.Id, Assert.Single(detail.Suggestions).Registration.Id);
        Assert.Equal(1, (await FollowUp(ctx, w).ListAsync(null, 0, 25)).Value!.Total);
        Assert.Null(detail.LinkedVendor);

        var wrongYear = await FollowUp(ctx, w).ResolveAsync(new(Guid.NewGuid(), id, VendorRegistrationId: lastYear.Id));
        Assert.Equal(ResultStatus.Conflict, wrongYear.Status);
        Assert.Equal(0, await ctx.FishMeatDirectCollectionLinks.CountAsync(l => l.CollectionId == id));

        var linked = await FollowUp(ctx, w).ResolveAsync(new(Guid.NewGuid(), id, VendorRegistrationId: thisYear.Id, Note: "Checked the permit"));
        Assert.True(linked.IsSuccess, linked.Error);
        Assert.False(linked.Value!.Registered);
        Assert.Equal("Mark Lorgadar", (await FollowUp(ctx, w).DetailAsync(id)).Value!.LinkedVendor);
        Assert.Equal(1, await ctx.FishMeatVendorRegistrations.CountAsync(x => x.MunicipalityId == w.TenantId && x.TaxYear == Today.Year));   // no duplicate registration
    }

    [SkippableFact]
    public async Task OnlyTheHeadResolves_AndAnotherOfficeSeesNothing()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await PostPayerOnlyAsync(ctx, w, "Giorgia Macapagal");

        var admin = await FollowUp(ctx, w, "Admin").ResolveAsync(new(Guid.NewGuid(), id, Register: new(FishMeatVendorType.Fish, "Giorgia Macapagal")));
        Assert.Equal(ResultStatus.Forbidden, admin.Status);
        Assert.Equal(ResultStatus.Invalid, (await FollowUp(ctx, w).ResolveAsync(new(Guid.NewGuid(), id))).Status);   // neither a vendor nor a registration

        await using var otherCtx = db.CreateContext(other.TenantId);
        Assert.Empty((await FollowUp(otherCtx, other).ListAsync(null, 0, 25)).Value!.Rows);
        Assert.Equal(ResultStatus.NotFound, (await FollowUp(otherCtx, other).DetailAsync(id)).Status);
        Assert.Equal(ResultStatus.NotFound, (await Review(otherCtx, other).GetDetailAsync(id)).Status);
    }

    [SkippableFact]
    public async Task TheDetailOpensTheCollectionWithItsHistory()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var id = await PostPayerOnlyAsync(ctx, w, "Giorgia Macapagal");
        Assert.True((await Review(ctx, w).ReviewAsync(new(Guid.NewGuid(), id, "NeedsCorrection", "Amount looks short"))).IsSuccess);

        var detail = (await Review(ctx, w).GetDetailAsync(id)).Value!;
        Assert.Equal(id, detail.Row.CollectionId);
        Assert.Equal(43m, detail.Row.Amount);
        Assert.Equal("Needs registration", detail.Row.Registration);
        Assert.Equal("Fish / Meat Vendor Fee", Assert.Single(detail.Lines).Description);
        var step = Assert.Single(detail.History);
        Assert.Equal("Needs correction", step.Outcome);
        Assert.Equal("Amount looks short", step.Note);
    }
}
