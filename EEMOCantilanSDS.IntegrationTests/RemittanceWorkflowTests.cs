using EEMOCantilanSDS.Application.Common;
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
    public async Task ACollectorReadsOnlyTheirOwnPosition_FromTheTokenIdentity_WithFormCountsAndNoOfficeData()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("mine");
        var other = await SeedAsync("other");
        await PostThreeAsync(w);
        await PostThreeAsync(other);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        RemittanceWorkflow AsCollector(Guid id, Guid tenant, AppDbContext c) =>
            new(c, new Caller(id, tenant, "Collector"), new FixedTenant(tenant));

        var mine = (await AsCollector(w.Collector.Id, w.Tenant.Id, ctx).GetMyPositionAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Equal(w.Collector.Id, mine.CollectorId);
        Assert.Equal(110m, mine.Collected);                 // server-derived, the same figure the office position uses
        Assert.Equal(0m, mine.Remitted);
        Assert.Equal(110m, mine.Unremitted);
        var tickets = Assert.Single(mine.Forms);
        Assert.Equal((10, 3, 7), (tickets.Assigned, tickets.Issued, tickets.OnHand));

        // The office position for the same period agrees with the collector's own row.
        var office = (await Remit(ctx, w).GetPositionAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Equal(mine.Collected, office.Collectors.Single(x => x.CollectorId == w.Collector.Id).Collected);

        // Once the Head records the remittance, the collector's own position moves; nothing was typed by the collector.
        Assert.True((await Remit(ctx, w).RecordAsync(Request(w, 110m))).IsSuccess);
        var after = (await AsCollector(w.Collector.Id, w.Tenant.Id, ctx).GetMyPositionAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Equal((110m, 110m, 0m), (after.Collected, after.Remitted, after.Unremitted));

        // Another tenant's collector never sees this tenant's money, and this tenant's collector sees none of theirs.
        await using var otherCtx = db.CreateContext(other.Tenant.Id);
        var theirs = (await AsCollector(other.Collector.Id, other.Tenant.Id, otherCtx).GetMyPositionAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Equal(other.Collector.Id, theirs.CollectorId);
        Assert.Equal(110m, theirs.Collected);
        Assert.DoesNotContain(theirs.Forms, f => f.Assigned > 10);
        var crossTenant = await AsCollector(w.Collector.Id, other.Tenant.Id, otherCtx).GetMyPositionAsync(Today.AddDays(-1), Today);
        Assert.Equal(0m, crossTenant.Value!.Collected);     // wrong tenant scope finds nothing of the collector's
    }

    [SkippableFact]
    public async Task ACollectorReadsOnlyTheirOwnCanonicalCollections_AndAdministratorsUseTheOfficeRegisterInstead()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("reg");
        var other = await SeedAsync("reg2");
        await PostThreeAsync(w);
        await PostThreeAsync(other);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        CollectionsReportWorkflow As(Guid id, string role) =>
            new(ctx, new Caller(id, w.Tenant.Id, role), new FixedTenant(w.Tenant.Id));

        var mine = (await As(w.Collector.Id, "Collector").GetMyRegisterAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Equal(3, mine.Rows.Count);
        Assert.Equal(110m, mine.Net);
        Assert.All(mine.Rows, r => Assert.Equal(w.Collector.Id, r.CollectorId));
        Assert.All(mine.Rows, r => Assert.NotNull(r.DocumentNumber));

        // The other tenant's collector data is not visible from this tenant's scope.
        var none = (await As(other.Collector.Id, "Collector").GetMyRegisterAsync(Today.AddDays(-1), Today)).Value!;
        Assert.Empty(none.Rows);

        Assert.Equal(ResultStatus.Forbidden, (await As(w.HeadId, "Admin").GetMyRegisterAsync(Today, Today)).Status);
        Assert.Equal(ResultStatus.Invalid, (await As(w.Collector.Id, "Collector").GetMyRegisterAsync(Today, Today.AddDays(-1))).Status);
    }

    [SkippableFact]
    public async Task TheCollectorPositionRefusesAdministratorsAndUnauthenticatedCallers_AndAdminRemittanceStaysAdminOnly()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("auth");
        await using var ctx = db.CreateContext(w.Tenant.Id);

        var admin = new RemittanceWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
        Assert.Equal(ResultStatus.Forbidden, (await admin.GetMyPositionAsync(Today, Today)).Status);

        // A collector still cannot use the office position, scope or record.
        var collector = new RemittanceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
        Assert.Equal(ResultStatus.Forbidden, (await collector.GetPositionAsync(Today, Today)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await collector.RecordAsync(Request(w, 1m))).Status);

        // An invalid period is refused rather than silently widened.
        Assert.Equal(ResultStatus.Invalid, (await collector.GetMyPositionAsync(Today, Today.AddDays(-1))).Status);
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
    public async Task TwoUsersRemittingTheSameCollectionsAtOnce_ProduceExactlyOneActiveRemittance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PostThreeAsync(w);

        async Task<bool> TryAsync()
        {
            await using var ctx = db.CreateContext(w.Tenant.Id);
            return (await Remit(ctx, w).RecordAsync(Request(w, 110m))).IsSuccess;
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(TryAsync)));

        await using var verify = db.CreateContext(w.Tenant.Id);
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, await verify.CollectionRemittances.CountAsync());
        Assert.Equal(3, await verify.CollectionRemittanceCoverages.CountAsync(x => x.IsActive));
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

    // ── The collector's report facts: the canonical side of the Mobile report, exactly once (IA-050 / IA-052) ──

    private static readonly DateOnly MonthStart = new(Today.Year, Today.Month, 1);
    private static readonly DateOnly MonthEnd = MonthStart.AddMonths(1).AddDays(-1);

    private RemittanceWorkflow AsCollector(AppDbContext ctx, World w) =>
        new(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));

    [SkippableFact]
    public async Task APostedLandingTicket_IsInTheCollectorsMonth_Once_NetOfNothing_AndAgreesWithRecordsAndPosition()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("rpt");
        await PostAsync(w, 0, CollectorOperationCodes.LandingBerthing, 50m);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        var facts = (await AsCollector(ctx, w).GetMyCollectionsAsync(MonthStart, MonthEnd)).Value!;
        var fact = Assert.Single(facts);
        Assert.Equal((Today, w.Documents[0].DocumentNumber, 50m), (fact.BusinessDate, fact.DocumentNumber, fact.NetAmount));
        Assert.Equal("Landing/Berthing", Assert.Single(fact.Lines).Name);
        // The walk-up payer text is evidence, never an identity.
        Assert.Null(fact.PayorId);
        Assert.Null(fact.PayorName);

        // Records shows the same Collection; the report counts it once; Position states the same money.
        var records = (await new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id))
            .GetCollectorRecordsAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal(fact.CollectionId, Assert.Single(records).CollectionId);
        var position = (await AsCollector(ctx, w).GetMyPositionAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal(facts.Sum(x => x.NetAmount), position.Collected);

        // The month before does not contain it: selection is by business date, inclusive, with no UTC shift.
        var before = (await AsCollector(ctx, w).GetMyCollectionsAsync(MonthStart.AddMonths(-1), MonthStart.AddDays(-1))).Value!;
        Assert.Empty(before);
        var onlyThatDay = (await AsCollector(ctx, w).GetMyCollectionsAsync(Today, Today)).Value!;
        Assert.Single(onlyThatDay);
    }

    [SkippableFact]
    public async Task ARetriedClientOperation_IsOneCollection_AndOneReportContribution()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("retry");
        var operationId = Guid.NewGuid();
        var doc = w.Documents[0];
        var request = new GovernedServicePostRequest(1, operationId, CollectorOperationCodes.LandingBerthing, Today, 50m, null,
            "Walk-up", null, doc.Id, doc.DocumentNumber, DateTime.UtcNow.AddMinutes(-1));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var post = db.CreateContext(w.Tenant.Id);
            var result = await new GovernedServiceWorkflow(post, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id))
                .PostMobileAsync(request);
            Assert.True(result.IsSuccess, result.Error);
        }

        await using var ctx = db.CreateContext(w.Tenant.Id);
        var facts = (await AsCollector(ctx, w).GetMyCollectionsAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal(50m, Assert.Single(facts).NetAmount);
    }

    [SkippableFact]
    public async Task MarketFeesAndLanding_AddUp_ByClassification_AndARemittanceAddsNoRevenue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("mix");
        await PostAsync(w, 0, CollectorOperationCodes.MarketFees, 30m);
        await PostAsync(w, 1, CollectorOperationCodes.LandingBerthing, 50m);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        var facts = (await AsCollector(ctx, w).GetMyCollectionsAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal(80m, facts.Sum(x => x.NetAmount));
        Assert.Equal(["Landing/Berthing", "Market Fees"], facts.SelectMany(x => x.Lines).Select(l => l.Name).Order());

        var before = (await AsCollector(ctx, w).GetMyPositionAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal((80m, 0m, 80m), (before.Collected, before.Remitted, before.Unremitted));
        Assert.True((await Remit(ctx, w).RecordAsync(Request(w, 80m))).IsSuccess);

        var after = (await AsCollector(ctx, w).GetMyCollectionsAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal(80m, after.Sum(x => x.NetAmount));     // remittance never adds or removes collected revenue
        var position = (await AsCollector(ctx, w).GetMyPositionAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal((80m, 80m, 0m), (position.Collected, position.Remitted, position.Unremitted));
    }

    [SkippableFact]
    public async Task ACollectorReadsOnlyTheirOwnCollections_InTheirOwnTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("own");
        var other = await SeedAsync("theirs");
        await PostAsync(w, 0, CollectorOperationCodes.LandingBerthing, 50m);
        await PostAsync(other, 0, CollectorOperationCodes.LandingBerthing, 50m);
        await PostAsync(other, 1, CollectorOperationCodes.MarketFees, 30m);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        Assert.Equal(50m, (await AsCollector(ctx, w).GetMyCollectionsAsync(MonthStart, MonthEnd)).Value!.Sum(x => x.NetAmount));

        // An office role, or a token whose tenant claim disagrees with the resolved tenant, reads nothing.
        var office = await Remit(ctx, w).GetMyCollectionsAsync(MonthStart, MonthEnd);
        Assert.Equal(ResultStatus.Forbidden, office.Status);
        var crossed = await new RemittanceWorkflow(ctx, new Caller(w.Collector.Id, other.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id))
            .GetMyCollectionsAsync(MonthStart, MonthEnd);
        Assert.Equal(ResultStatus.Forbidden, crossed.Status);
    }

    [SkippableFact]
    public async Task TheMobileReport_StatesAPostedLandingTicket_InTotalsMonthAndClassification_WithoutInventingAPayee()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("mobrpt");
        await PostAsync(w, 0, CollectorOperationCodes.LandingBerthing, 50m);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        var caller = new Caller(w.Collector.Id, w.Tenant.Id, "Collector");
        var repository = new EEMOCantilanSDS.Infrastructure.Repositories.CollectorRepository(ctx);
        var handler = new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport.GetCollectorReportQueryHandler(
            repository, repository, AsCollector(ctx, w), caller, new EEMOCantilanSDS.Infrastructure.Time.SystemClock());

        var result = await handler.Handle(
            new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport.GetCollectorReportQuery(null, Today.Year, Today.Month),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var report = result.Value!;
        Assert.Equal((50m, 1), (report.Totals.CollectedAmount, report.Totals.TransactionCount));
        var month = Assert.Single(report.Periods);
        Assert.Equal((MonthStart, 50m, 1), (month.PeriodDate, month.CollectedAmount, month.TransactionCount));
        var line = Assert.Single(report.Breakdown!);
        Assert.Equal(("Landing/Berthing", true, 50m), (line.Label, line.IsClassification, line.Amount));
        Assert.Equal(0, report.Totals.PayeeCount);
        Assert.Equal((50m, 1), (report.Totals.UnnamedCollectedAmount, report.Totals.UnnamedTransactionCount));

        // Position, Records and the report agree on the one Collection.
        var position = (await AsCollector(ctx, w).GetMyPositionAsync(MonthStart, MonthEnd)).Value!;
        Assert.Equal(report.Totals.CanonicalCollectedAmount, position.Collected);

        // The previous month is unaffected.
        var previous = MonthStart.AddMonths(-1);
        var before = await handler.Handle(
            new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport.GetCollectorReportQuery(null, previous.Year, previous.Month),
            CancellationToken.None);
        Assert.Equal(0m, before.Value!.Totals.CollectedAmount);
    }

    // ── Revenue-source performance and the official statement: the same posted money, once ──

    private sealed class StatementClock : EEMOCantilanSDS.Application.Common.Interface.Time.IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    [SkippableFact]
    public async Task ALandingTicket_IsInTheStatementAndTheSourceRegister_Once_WithTransactionalFiguresOnly()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("rsp");
        await PostAsync(w, 0, CollectorOperationCodes.LandingBerthing, 50m);
        await PostAsync(w, 1, CollectorOperationCodes.MarketFees, 30m);

        await using var ctx = db.CreateContext(w.Tenant.Id);
        var head = new Caller(w.HeadId, w.Tenant.Id, "Admin");
        var statement = new EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome.GetOfficialMonthlyIncomeQueryHandler(
            ctx, new EEMOCantilanSDS.Infrastructure.Repositories.LegacyMonthlyIncomeReader(ctx), head, new FixedTenant(w.Tenant.Id), new StatementClock());
        var register = new EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance.GetRevenueSourcePerformanceQueryHandler(
            statement, ctx, new FixedTenant(w.Tenant.Id));

        var month = (await register.Handle(new(Today.Year, Today.Month), CancellationToken.None)).Value!;
        var landing = month.Rows.Single(r => r.Key == "LANDING_BERTHING");
        Assert.Equal((50m, 1, 1, 1), (landing.Collected, landing.TransactionCount, landing.DocumentCount, landing.CollectorCount));
        Assert.Equal((EEMOCantilanSDS.Application.Dtos.Revenue.RevenueSourceModel.Transactional, "Active", "CT"),
            (landing.Model, landing.Status, landing.Instruments));
        Assert.Null(landing.Facility);
        Assert.Equal(30m, month.Rows.Single(r => r.Key == "MARKET_FEES").Collected);

        // Every source the statement knows is listed, facility or not, and an empty source states nothing, not a rate.
        foreach (var key in new[] { "ECF", "WCF", "TABO", "WEIGHT_AND_MEASURE", "TRANSFER_LARGE_CATTLE", "ICE_PLANT", "RENT_NPM",
                     "RENT_NCC", "RENT_TCC", "VEGETABLE_FRUIT_SPACE_RENTAL", "KANMANGGAY_SPACE_RENTAL", "FIESTA_ARAW_LOT_RENTAL", "PENALTIES_AND_FINES", "ARREARS" })
            Assert.Contains(month.Rows, r => r.Key == key);
        Assert.Equal("Nothing recorded", month.Rows.Single(r => r.Key == "TRANSFER_LARGE_CATTLE").Status);

        // The register's total is the statement's total for the month; the statement's year total is its months added up.
        var year = (await statement.Handle(new(Today.Year, null), CancellationToken.None)).Value!;
        Assert.Equal(year.MonthTotals[Today.Month - 1].Total, month.TotalCollected);
        Assert.Equal(year.GrandTotal.Total, year.MonthTotals.Sum(c => c.Total));
        var landingRow = year.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "LANDING_BERTHING");
        Assert.Equal(50m, landingRow.Months[Today.Month - 1].Total);
        Assert.Equal(landingRow.Total.Total, landingRow.Months.Sum(c => c.Total));
        Assert.Null(landingRow.AnnualTarget);
        Assert.Null(landingRow.Attainment);

        // A remittance is not revenue: covering both tickets changes neither report.
        Assert.True((await Remit(ctx, w).RecordAsync(Request(w, 80m))).IsSuccess);
        var after = (await register.Handle(new(Today.Year, Today.Month), CancellationToken.None)).Value!;
        Assert.Equal(month.TotalCollected, after.TotalCollected);
    }

    // ── Several collectors at once: independent records, one save (multi-collector New Remittance) ──

    /// <summary>Adds a second collector to the tenant with their own Cash Ticket book and three posted collections (₱110).</summary>
    private async Task<CollectorUser> AddSecondCollectorWithCollectionsAsync(World w)
    {
        var ben = CollectorUser.Create("Ben Cruz", "C-02", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), w.Tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Add(ben);
            await setup.SaveChangesAsync();
        }
        List<AccountableDocument> documents;
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            ctx.Add(CollectorOperationAssignment.Assign(w.Tenant.Id, ben.Id, CollectorOperationCodes.MarketFees, "head"));
            ctx.Add(CollectorOperationAssignment.Assign(w.Tenant.Id, ben.Id, CollectorOperationCodes.LandingBerthing, "head"));
            await ctx.SaveChangesAsync();
            var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "SuperAdmin"), new FixedTenant(w.Tenant.Id));
            var book = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(
                RevenueInstrumentType.CashTicket, "CT book B", "CTB", 1, 5, 6))).Value!;
            Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(book.BookId, ben.Id, 1, 5))).IsSuccess);
            documents = await ctx.AccountableDocuments.Where(x => x.FormBookId == book.BookId).OrderBy(x => x.SerialNumber).ToListAsync();
        }
        var asBen = w with { Collector = ben, Documents = documents };
        await PostThreeAsync(asBen);
        return ben;
    }

    private static RecordRemittanceRequest RequestFor(Guid collectorId, decimal amount, string reference, Guid? operationId = null) => new(
        operationId ?? Guid.NewGuid(), collectorId, Today, Today.AddDays(-1), Today, RevenueInstrumentType.CashTicket, null, amount, reference, null);

    [SkippableFact]
    public async Task ABatchRecordsOneIndependentRemittancePerCollector_EachCoveringOnlyTheirCollections_AndNoRevenue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("batch");
        await PostThreeAsync(w);
        var ben = await AddSecondCollectorWithCollectionsAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var collectionsBefore = await ctx.Collections.CountAsync();
        var (anaOp, benOp) = (Guid.NewGuid(), Guid.NewGuid());
        var request = new RecordRemittanceBatchRequest([
            RequestFor(w.Collector.Id, 110m, "ACK-A", anaOp),
            RequestFor(ben.Id, 100m, "ACK-B", benOp)]);

        var result = await Remit(ctx, w).RecordBatchAsync(request);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        var rows = await ctx.CollectionRemittances.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        var ana = rows.Single(x => x.CollectorId == w.Collector.Id);
        var benRow = rows.Single(x => x.CollectorId == ben.Id);
        Assert.Equal((110m, "ACK-A"), (ana.RemittedAmount, ana.Reference));
        Assert.Equal((100m, 10m, "ACK-B"), (benRow.RemittedAmount, benRow.ExpectedAmount - benRow.RemittedAmount, benRow.Reference));
        // Every collection is covered exactly once, by its own collector's record; no collection or revenue was created.
        var coverage = await ctx.CollectionRemittanceCoverages.AsNoTracking().ToListAsync();
        Assert.Equal(6, coverage.Count);
        Assert.Equal(6, coverage.Select(x => x.CollectionId).Distinct().Count());
        Assert.Equal(collectionsBefore, await ctx.Collections.CountAsync());

        // A retry with the same identities returns the same records and writes nothing new.
        await using var retryCtx = db.CreateContext(w.Tenant.Id);
        var retry = await Remit(retryCtx, w).RecordBatchAsync(request);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(result.Value.Select(x => x.Row.Id).Order(), retry.Value!.Select(x => x.Row.Id).Order());
        Assert.Equal(2, await retryCtx.CollectionRemittances.CountAsync());
    }

    [SkippableFact]
    public async Task ABatchWithOneInvalidCollector_RecordsNothing_AndNamesTheCollector()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("batchbad");
        await PostThreeAsync(w);
        var ben = await AddSecondCollectorWithCollectionsAsync(w);
        await using var ctx = db.CreateContext(w.Tenant.Id);

        var overpaid = await Remit(ctx, w).RecordBatchAsync(new RecordRemittanceBatchRequest([
            RequestFor(w.Collector.Id, 110m, "ACK-A"),
            RequestFor(ben.Id, 500m, "ACK-B")]));
        Assert.False(overpaid.IsSuccess);
        Assert.Contains("Ben Cruz", overpaid.Error);
        Assert.Contains("Nothing was recorded", overpaid.Error);

        // The same collector twice is refused before any check, and Ana's otherwise valid remittance was not saved.
        var twice = await Remit(ctx, w).RecordBatchAsync(new RecordRemittanceBatchRequest([
            RequestFor(w.Collector.Id, 50m, "X"), RequestFor(w.Collector.Id, 60m, "Y")]));
        Assert.False(twice.IsSuccess);
        await using var check = db.CreateContext(w.Tenant.Id);
        Assert.Equal(0, await check.CollectionRemittances.CountAsync());
        Assert.Equal(0, await check.CollectionRemittanceCoverages.CountAsync());
    }

    [SkippableFact]
    public async Task ABatchRacingASingleRemittanceForTheSameCollections_NeverCoversACollectionTwice()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("batchrace");
        await PostThreeAsync(w);
        var ben = await AddSecondCollectorWithCollectionsAsync(w);
        await using var first = db.CreateContext(w.Tenant.Id);
        await using var second = db.CreateContext(w.Tenant.Id);

        var results = await Task.WhenAll(
            Remit(first, w).RecordBatchAsync(new RecordRemittanceBatchRequest([
                RequestFor(w.Collector.Id, 110m, "B-A"), RequestFor(ben.Id, 110m, "B-B")])).ContinueWith(t => t.Result.IsSuccess),
            Remit(second, w).RecordAsync(RequestFor(ben.Id, 110m, "S-B")).ContinueWith(t => t.Result.IsSuccess));

        Assert.Contains(true, results);
        await using var check = db.CreateContext(w.Tenant.Id);
        var active = await check.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        Assert.Equal(active.Count, active.Select(x => x.CollectionId).Distinct().Count());
    }
}
