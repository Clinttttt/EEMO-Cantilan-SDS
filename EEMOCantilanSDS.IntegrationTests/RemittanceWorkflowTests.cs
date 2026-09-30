using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Remittance and liquidation (IA-052): money already collected and turned over, covering whole posted Collections exactly
/// once. A remittance creates no revenue, does not depend on exhausting the assigned Cash Tickets, keeps a shortfall visible
/// and is idempotent. Form return and spoilage never make a form available again or turn it into money.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RemittanceWorkflowTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "collector" : "office";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-remit";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(Municipality Tenant, CollectorUser Collector, Guid HeadId, Guid BookId, List<AccountableDocument> Documents);

    private static readonly DateOnly Today = PhilippineTime.Today;

    private async Task<World> SeedAsync(string label = "rem")
    {
        var tenant = Municipality.Create($"{label}-{Guid.NewGuid():N}"[..12], $"Remit {label}", "Province",
            MunicipalityStatus.Active, tenantCode: $"remit-{label}-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        var effective = Today.AddDays(-30);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        var headId = Guid.NewGuid();
        Guid bookId;
        List<AccountableDocument> documents;
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            foreach (var (code, name, amount) in new[]
                     {
                         (RevenueClassificationCodes.MarketFees, CollectorOperationCodes.MarketFees, 30m),
                         (RevenueClassificationCodes.LandingBerthing, CollectorOperationCodes.LandingBerthing, 50m)
                     })
            {
                var classification = RevenueClassification.Create(code, tenant.Id);
                ctx.Add(classification);
                ctx.Add(RevenueClassificationPolicy.Create(classification.Id, effective, code == RevenueClassificationCodes.MarketFees ? "Market Fees" : "Landing/Berthing",
                    RevenueInstrumentType.CashTicket, tenant.Id));
                var service = GovernedService.Create(tenant.Id, name, "head");
                ctx.Add(service);
                ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, effective, GovernedServiceBasis.FixedAmount, amount, null, true, true, "head"));
                ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, name, "head"));
            }
            await ctx.SaveChangesAsync();
            var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(headId, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
            var book = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(
                RevenueInstrumentType.CashTicket, "CT book", "CT", 1, 10, 6))).Value!;
            Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(book.BookId, collector.Id, 1, 10))).IsSuccess);
            bookId = book.BookId;
            documents = await ctx.AccountableDocuments.OrderBy(x => x.SerialNumber).ToListAsync();
        }
        return new World(tenant, collector, headId, bookId, documents);
    }

    private async Task PostAsync(World w, int documentIndex, string operationCode, decimal amount)
    {
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
        var doc = w.Documents[documentIndex];
        var result = await workflow.PostMobileAsync(new GovernedServicePostRequest(
            1, Guid.NewGuid(), operationCode, Today, amount, null, "Walk-up", null, doc.Id, doc.DocumentNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.True(result.IsSuccess, result.Error);
    }

    private RemittanceWorkflow Remit(AppDbContext ctx, World w, string role = "Admin", Guid? tenant = null) =>
        new(ctx, new Caller(w.HeadId, tenant ?? w.Tenant.Id, role), new FixedTenant(tenant ?? w.Tenant.Id));

    private static RecordRemittanceRequest Request(World w, decimal amount, Guid? operationId = null, IReadOnlyList<Guid>? ids = null) => new(
        operationId ?? Guid.NewGuid(), w.Collector.Id, Today, Today.AddDays(-1), Today, RevenueInstrumentType.CashTicket, ids, amount, "ACK-1", null);

    private async Task PostThreeAsync(World w)
    {
        await PostAsync(w, 0, CollectorOperationCodes.MarketFees, 30m);
        await PostAsync(w, 1, CollectorOperationCodes.MarketFees, 30m);
        await PostAsync(w, 2, CollectorOperationCodes.LandingBerthing, 50m);
    }

    [SkippableFact]
    public async Task ACollectorRemitsTheMoneyCollected_WhileTheUnusedTicketsStayAssigned_AndNoRevenueIsCreated()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var remit = Remit(ctx, w);

        var scope = (await remit.GetScopeAsync(w.Collector.Id, Today.AddDays(-1), Today, RevenueInstrumentType.CashTicket)).Value!;
        Assert.Equal(110m, scope.ExpectedAmount);                       // derived, never typed
        Assert.Equal(new[] { "Landing/Berthing", "Market Fees" }, scope.Breakdown.Select(x => x.Name));
        Assert.Equal(new[] { 50m, 60m }, scope.Breakdown.Select(x => x.Amount));

        var collections = await ctx.Collections.CountAsync();
        var lines = await ctx.CollectionLines.CountAsync();
        var recorded = await remit.RecordAsync(Request(w, 110m));

        Assert.True(recorded.IsSuccess, recorded.Error);
        Assert.Equal((110m, 110m, 0m), (recorded.Value!.Row.ExpectedAmount, recorded.Value.Row.RemittedAmount, recorded.Value.Row.DifferenceAmount));
        Assert.Equal(3, recorded.Value.Collections.Count);
        // No new revenue event: the remittance wrote no Collection and no line.
        Assert.Equal(collections, await ctx.Collections.CountAsync());
        Assert.Equal(lines, await ctx.CollectionLines.CountAsync());
        // Seven tickets are still on hand, assigned to the collector, untouched by the remittance.
        var stock = await ctx.AccountableDocuments.CountAsync(x => x.State == AccountableDocumentState.Assigned && x.AssignedUserId == w.Collector.Id);
        Assert.Equal(7, stock);

        var position = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!;
        var row = Assert.Single(position.Collectors);
        Assert.Equal((110m, 110m, 0m), (row.Collected, row.Remitted, row.Unremitted));
        var forms = Assert.Single(row.Forms);
        Assert.Equal((10, 3, 0, 0, 0, 7, 10), (forms.Assigned, forms.Issued, forms.Spoiled, forms.Returned, forms.NeedsReview, forms.OnHand, forms.AccountedFor));
    }

    [SkippableFact]
    public async Task ACollectionCannotBeRemittedTwice_AndTheDatabaseRefusesASecondActiveCoverage()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var remit = Remit(ctx, w);
        var first = await remit.RecordAsync(Request(w, 110m));
        Assert.True(first.IsSuccess, first.Error);

        var second = await remit.RecordAsync(Request(w, 110m));
        Assert.False(second.IsSuccess);
        Assert.Contains("no unremitted collection", second.Error, StringComparison.OrdinalIgnoreCase);

        // Even bypassing the workflow, the partial unique index refuses two active coverages of one Collection.
        await using var raw = db.CreateContext(w.Tenant.Id);
        var covered = await raw.CollectionRemittanceCoverages.FirstAsync();
        var other = CollectionRemittance.Record(w.Tenant.Id, w.Collector.Id, Today, Today, Today, null, 30m, 30m, 1, null, null,
            Guid.NewGuid(), "x", "office", "actor", DateTime.UtcNow);
        raw.CollectionRemittances.Add(other);
        raw.CollectionRemittanceCoverages.Add(CollectionRemittanceCoverage.Cover(w.Tenant.Id, other.Id, covered.CollectionId, 30m));
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task AShortfallStaysVisibleAsADifference_AnExcessIsRefused_AndTheCollectionsAreNeverAdjusted()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var remit = Remit(ctx, w);

        var over = await remit.RecordAsync(Request(w, 120m));
        Assert.False(over.IsSuccess);
        Assert.Contains("cannot exceed", over.Error, StringComparison.OrdinalIgnoreCase);

        var short_ = await remit.RecordAsync(Request(w, 100m));
        Assert.True(short_.IsSuccess, short_.Error);
        Assert.Equal((110m, 100m, 10m), (short_.Value!.Row.ExpectedAmount, short_.Value.Row.RemittedAmount, short_.Value.Row.DifferenceAmount));
        Assert.True(short_.Value.NeedsReview);
        Assert.Equal(110m, await ctx.Collections.SumAsync(x => x.TotalAmount));   // source collections untouched
        var position = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Equal(1, position.NeedsReviewCount);
    }

    [SkippableFact]
    public async Task ARetryWithTheSameOperationIdReturnsTheSameRemittance_AndAChangedIntentIsAConflict()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var remit = Remit(ctx, w);
        var operation = Guid.NewGuid();

        var first = await remit.RecordAsync(Request(w, 110m, operation));
        var retry = await remit.RecordAsync(Request(w, 110m, operation));
        var changed = await remit.RecordAsync(Request(w, 100m, operation));

        Assert.True(first.IsSuccess && retry.IsSuccess, first.Error ?? retry.Error);
        Assert.Equal(first.Value!.Row.Id, retry.Value!.Row.Id);
        Assert.Equal(1, await ctx.CollectionRemittances.CountAsync());
        Assert.False(changed.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, changed.Status);
    }

    [SkippableFact]
    public async Task VoidingARemittanceFreesItsCollections_KeepsItsHistory_AndAllowsTheCorrectRecord()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var remit = Remit(ctx, w);
        var wrong = (await remit.RecordAsync(Request(w, 100m))).Value!;

        Assert.False((await remit.VoidAsync(wrong.Row.Id, new VoidRemittanceRequest(" "))).IsSuccess);
        var voided = await remit.VoidAsync(wrong.Row.Id, new VoidRemittanceRequest("Counted short by mistake"));
        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(RemittanceStatus.Voided, voided.Value!.Row.Status);
        Assert.Equal(1, await ctx.CollectionRemittances.CountAsync());                    // history stays
        Assert.False((await remit.VoidAsync(wrong.Row.Id, new VoidRemittanceRequest("again"))).IsSuccess);

        var right = await remit.RecordAsync(Request(w, 110m));
        Assert.True(right.IsSuccess, right.Error);
        Assert.Equal(2, await ctx.CollectionRemittances.CountAsync());
    }

    [SkippableFact]
    public async Task ATenantCanNeitherSeeNorRemitAnotherTenantsCollections_AndOnlyOfficeStaffRecord()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedAsync("ta");
        var b = await SeedAsync("tb");
        await PostThreeAsync(a);
        await using var ctxB = db.CreateContext(b.Tenant.Id);
        var asB = Remit(ctxB, b);

        Assert.Equal(ResultStatus.NotFound, (await asB.GetScopeAsync(a.Collector.Id, Today.AddDays(-1), Today, null)).Status);
        var stolen = await asB.RecordAsync(Request(a, 110m));
        Assert.False(stolen.IsSuccess);
        Assert.Empty((await asB.GetRegisterAsync(Today.AddDays(-1), Today, null, null, null)).Value!);
        var positionB = (await asB.GetPositionAsync(Today.AddDays(-1), Today)).Value!;
        Assert.DoesNotContain(positionB.Collectors, x => x.CollectorId == a.Collector.Id);
        Assert.Equal(0m, positionB.Collected);

        await using var ctxA = db.CreateContext(a.Tenant.Id);
        var asCollector = Remit(ctxA, a, role: "Collector");
        Assert.Equal(ResultStatus.Forbidden, (await asCollector.RecordAsync(Request(a, 110m))).Status);
    }

    [SkippableFact]
    public async Task UnusedFormsReturnToOffice_SpoiledBlankFormsAreNotMoney_AndAnIssuedTicketNeverBecomesAvailable()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));

        // An issued ticket cannot return or be spoiled.
        Assert.False((await custody.ReturnUnusedAsync(new ReturnUnusedFormsRequest(w.BookId, 1, 5))).IsSuccess);
        Assert.False((await custody.SpoilAsync(new SpoilFormRequest(w.Documents[0].Id, "torn", null))).IsSuccess);
        Assert.Equal(AccountableDocumentState.Consumed, (await ctx.AccountableDocuments.AsNoTracking().SingleAsync(x => x.Id == w.Documents[0].Id)).State);

        Assert.True((await custody.SpoilAsync(new SpoilFormRequest(w.Documents[3].Id, "Torn while writing", "before any payer"))).IsSuccess);
        Assert.False((await custody.SpoilAsync(new SpoilFormRequest(w.Documents[3].Id, "again", null))).IsSuccess);
        Assert.False((await custody.SpoilAsync(new SpoilFormRequest(w.Documents[4].Id, " ", null))).IsSuccess);
        Assert.Equal(1, (await custody.GetSpoiledAsync()).Value!.Count);
        var returned = await custody.ReturnUnusedAsync(new ReturnUnusedFormsRequest(w.BookId, 6, 8));
        Assert.True(returned.IsSuccess, returned.Error);
        Assert.Equal(3, returned.Value);
        // A spoiled form is not returnable, and a returned one is back in office custody with its assignment closed.
        Assert.False((await custody.ReturnUnusedAsync(new ReturnUnusedFormsRequest(w.BookId, 4, 4))).IsSuccess);
        Assert.Equal(AccountableDocumentState.Voided, (await ctx.AccountableDocuments.AsNoTracking().SingleAsync(x => x.Id == w.Documents[3].Id)).State);
        Assert.Equal(AccountableDocumentState.InOffice, (await ctx.AccountableDocuments.AsNoTracking().SingleAsync(x => x.Id == w.Documents[6].Id)).State);

        // None of it is money, and the position accounts for every form: 3 issued + 1 spoiled + 3 returned + 3 on hand.
        Assert.Equal(3, await ctx.Collections.CountAsync());
        var remit = Remit(ctx, w);
        var forms = Assert.Single(Assert.Single((await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors).Forms);
        Assert.Equal((10, 3, 1, 3, 0, 3, 10), (forms.Assigned, forms.Issued, forms.Spoiled, forms.Returned, forms.NeedsReview, forms.OnHand, forms.AccountedFor));
    }
}
