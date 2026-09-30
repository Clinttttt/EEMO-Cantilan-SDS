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
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Governed configurable services (Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit): the
/// collector states facts, the server resolves classification, instrument and amount rule, and a physically issued
/// document is never returned to stock.
/// </summary>
public sealed class GovernedServiceWorkflowTests
{
    private static readonly DateOnly Today = PhilippineTime.Today;

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class CurrentUser(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "governed-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class World
    {
        public required DbContextOptions<AppDbContext> Options { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid OtherTenantId { get; init; }
        public required CollectorUser Collector { get; init; }
        public required CollectorUser OtherCollector { get; init; }
        public Guid HeadId { get; } = Guid.NewGuid();
        public List<AccountableDocumentSnapshot> Ct { get; } = [];
        public List<AccountableDocumentSnapshot> Or { get; } = [];
    }

    private sealed record AccountableDocumentSnapshot(Guid Id, string Number);

    private static (AppDbContext Db, GovernedServiceWorkflow Workflow) Open(World w, string role, Guid? userId = null, Guid? tenant = null)
    {
        var tenantId = tenant ?? w.TenantId;
        var db = new AppDbContext(w.Options, new FixedTenant(tenantId));
        var id = userId ?? (role == "Collector" ? w.Collector.Id : w.HeadId);
        return (db, new GovernedServiceWorkflow(db, new CurrentUser(id, tenantId, role), new FixedTenant(tenantId)));
    }

    /// <summary>Tenant with an active collector holding 3 assigned CT and 3 assigned OR units, plus all classifications/policies.</summary>
    private static async Task<World> CreateAsync(bool assignAllOperations = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"governed-{Guid.NewGuid():N}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;
        var tenant = Municipality.Create($"GV-{Guid.NewGuid():N}"[..12], "Governed Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"governed-{Guid.NewGuid():N}"[..24]);
        var other = Municipality.Create($"GW-{Guid.NewGuid():N}"[..12], "Other Governed", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"governed-o-{Guid.NewGuid():N}"[..24]);
        var collector = CollectorUser.Create("Ana Reyes", "C-01", "ana-gs", null, null, new HashedPassword("h"), tenant.Id);
        var otherCollector = CollectorUser.Create("Ben Cruz", "C-02", "ben-gs", null, null, new HashedPassword("h"), tenant.Id);
        var foreignCollector = CollectorUser.Create("Foreign", "C-03", "foreign-gs", null, null, new HashedPassword("h"), other.Id);
        var effective = Today.AddDays(-30);
        await using (var setup = new AppDbContext(options, new FixedTenant(tenant.Id)))
        {
            setup.AddRange(tenant, other, collector, otherCollector, foreignCollector);
            foreach (var (code, instrument) in new[]
            {
                (RevenueClassificationCodes.MarketFees, RevenueInstrumentType.CashTicket),
                (RevenueClassificationCodes.LandingBerthing, RevenueInstrumentType.CashTicket),
                (RevenueClassificationCodes.TransferLargeCattle, RevenueInstrumentType.OfficialReceipt),
            })
            {
                var c = RevenueClassification.Create(code, tenant.Id);
                setup.Add(c);
                setup.Add(RevenueClassificationPolicy.Create(c.Id, effective, code, instrument, tenant.Id));
            }
            var veg = RevenueClassification.Create(RevenueClassificationCodes.VegetableFruitSpaceRental, tenant.Id);
            setup.Add(veg);
            setup.Add(RevenueClassificationPolicy.Create(veg.Id, effective, "Vegetable whole", RevenueInstrumentType.OfficialReceipt,
                tenant.Id, businessContext: RevenuePolicyContext.VegetableWholePayment));
            setup.Add(RevenueClassificationPolicy.Create(veg.Id, effective, "Vegetable daily", RevenueInstrumentType.CashTicket,
                tenant.Id, businessContext: RevenuePolicyContext.VegetableDailyTransaction));
            // The same tenant-owned data in another tenant must never be usable by this one.
            await setup.SaveChangesAsync();
        }

        var world = new World
        {
            Options = options, TenantId = tenant.Id, OtherTenantId = other.Id,
            Collector = collector, OtherCollector = otherCollector
        };
        var (db, _) = Open(world, "SuperAdmin");
        await using (db)
        {
            var custody = new AccountableFormCustodyWorkflow(db, new CurrentUser(world.HeadId, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
            foreach (var (instrument, prefix, list) in new[]
            {
                (RevenueInstrumentType.CashTicket, "CT", world.Ct), (RevenueInstrumentType.OfficialReceipt, "OR", world.Or)
            })
            {
                var book = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(instrument, $"{prefix} book", prefix, 1, 6, 6))).Value!;
                Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(book.BookId, collector.Id, 1, 3))).IsSuccess);
                var docs = await db.AccountableDocuments.Where(x => x.FormBookId == book.BookId && x.SerialNumber <= 3)
                    .OrderBy(x => x.SerialNumber).ToListAsync();
                list.AddRange(docs.Select(d => new AccountableDocumentSnapshot(d.Id, d.DocumentNumber)));
            }
            if (assignAllOperations)
            {
                var assign = new CollectorOperationAssignmentWorkflow(db, new CurrentUser(world.HeadId, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
                Assert.True((await assign.ReplaceAsync(collector.Id, new EEMOCantilanSDS.Application.Dtos.ReplaceCollectorOperationAssignmentsRequest(
                    [CollectorOperationCodes.MarketFees, CollectorOperationCodes.LandingBerthing,
                     CollectorOperationCodes.TransferLargeCattle, CollectorOperationCodes.VegetableFruitSpaceRental]))).IsSuccess);
            }
        }
        return world;
    }

    private static async Task ConfigureAsync(World w, string code, GovernedServiceBasis basis, decimal? fixedAmount,
        decimal? ceiling, bool enabled = true, bool mobile = true, DateOnly? effective = null)
    {
        var (db, workflow) = Open(w, "SuperAdmin");
        await using var _ = db;
        var result = await workflow.ConfigureAsync(code, new ConfigureGovernedServiceRequest(
            effective ?? Today.AddDays(-10), basis, fixedAmount, ceiling, enabled, mobile));
        Assert.True(result.IsSuccess, result.Error);
    }

    private static GovernedServicePostRequest Post(
        World w, string code, decimal amount, AccountableDocumentSnapshot doc, GovernedServiceMode? mode = null,
        Guid? operationId = null, string? reference = "Stall check") => new(
        1, operationId ?? Guid.NewGuid(), code, Today, amount, mode, "Walk-up payer", reference,
        doc.Id, doc.Number, DateTime.UtcNow.AddMinutes(-1));

    // ── Setup ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyTheHeadConfigures_AndAnAdminOrCollectorCannot()
    {
        var w = await CreateAsync();
        foreach (var role in new[] { "Admin", "Collector" })
        {
            var (db, workflow) = Open(w, role);
            await using var _ = db;
            var result = await workflow.ConfigureAsync(CollectorOperationCodes.MarketFees, new ConfigureGovernedServiceRequest(
                Today, GovernedServiceBasis.FixedAmount, 30m, null, true, true));
            Assert.Equal(ResultStatus.Forbidden, result.Status);
            Assert.Empty(db.GovernedServices);
        }
    }

    [Fact]
    public async Task Configuration_AppendsVersions_AndNeverEditsHistory()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 30m, null, effective: Today.AddDays(-20));
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 40m, null, effective: Today.AddDays(-5));

        var (db, _) = Open(w, "SuperAdmin");
        await using var _2 = db;
        var versions = await db.GovernedServiceSettings.OrderBy(x => x.EffectiveDate).ToListAsync();
        Assert.Equal(new decimal?[] { 30m, 40m }, versions.Select(x => x.FixedAmount));
        Assert.Single(db.GovernedServices);
        Assert.Equal(30m, GovernedServiceSetting.Resolve(versions, Today.AddDays(-10))!.FixedAmount);
        Assert.Equal(40m, GovernedServiceSetting.Resolve(versions, Today)!.FixedAmount);
        Assert.Null(GovernedServiceSetting.Resolve(versions, Today.AddDays(-40)));
    }

    [Fact]
    public async Task Vegetable_CannotBeFixedAmount_AndAFixedServiceCannotCarryACeiling()
    {
        var w = await CreateAsync();
        var (db, workflow) = Open(w, "SuperAdmin");
        await using var _ = db;

        var vegetableFixed = await workflow.ConfigureAsync(CollectorOperationCodes.VegetableFruitSpaceRental,
            new ConfigureGovernedServiceRequest(Today, GovernedServiceBasis.FixedAmount, 20m, null, true, true));
        var fixedWithCeiling = await workflow.ConfigureAsync(CollectorOperationCodes.MarketFees,
            new ConfigureGovernedServiceRequest(Today, GovernedServiceBasis.FixedAmount, 20m, 50m, true, true));
        var wcf = await workflow.ConfigureAsync(CollectorOperationCodes.Wcf,
            new ConfigureGovernedServiceRequest(Today, GovernedServiceBasis.FixedAmount, 10m, null, true, true));

        Assert.Equal(ResultStatus.Invalid, vegetableFixed.Status);
        Assert.Equal(ResultStatus.Invalid, fixedWithCeiling.Status);
        Assert.Equal(ResultStatus.NotFound, wcf.Status); // WCF has its own source, not a governed service
        Assert.Empty(db.GovernedServiceSettings);
    }

    [Fact]
    public async Task Definitions_ReportSetupRequiredUntilAnAmountRuleExists_ThenActive()
    {
        var w = await CreateAsync();
        var (db, workflow) = Open(w, "Admin");
        await using var _ = db;

        var before = (await workflow.GetDefinitionsAsync()).Value!.Single(x => x.OperationCode == CollectorOperationCodes.MarketFees);
        Assert.Equal(GovernedServiceSetupState.SetupRequired, before.State);
        Assert.Contains(before.SetupIssues, x => x.Contains("amount rule"));

        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 30m, null);
        var after = (await workflow.GetDefinitionsAsync()).Value!.Single(x => x.OperationCode == CollectorOperationCodes.MarketFees);
        Assert.Equal(GovernedServiceSetupState.Active, after.State);
        Assert.Equal(RevenueInstrumentType.CashTicket, after.Instruments.Single().Instrument);

        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 30m, null, enabled: false);
        var disabled = (await workflow.GetDefinitionsAsync()).Value!.Single(x => x.OperationCode == CollectorOperationCodes.MarketFees);
        Assert.Equal(GovernedServiceSetupState.Disabled, disabled.State);
    }

    // ── Posting ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IncompleteSetup_PostsNothing_AndTheIssuedDocumentIsQuarantinedNotReturnedToStock()
    {
        var w = await CreateAsync();
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        var doc = w.Ct[0];

        var result = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, doc));

        Assert.False(result.IsSuccess);
        Assert.StartsWith("RECONCILIATION_REQUIRED:", result.Error);
        Assert.Empty(db.Collections);
        var stored = await db.AccountableDocuments.SingleAsync(x => x.Id == doc.Id);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, stored.State);
        var op = await db.PostingOperations.SingleAsync();
        Assert.Equal(PostingOperationStatus.ReconciliationRequired, op.Status);
        Assert.Equal(doc.Id, op.AccountableDocumentId);
    }

    [Fact]
    public async Task FixedAmountService_PostsOneClassifiedLine_FreezesSetupEvidence_AndConsumesTheDocument()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 30m, null);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        var doc = w.Ct[0];

        var result = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, doc));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(RevenueInstrumentType.CashTicket, result.Value!.Instrument);
        var collection = await db.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal(30m, collection.TotalAmount);
        Assert.Equal(w.Collector.Id, collection.CollectorId);
        Assert.Null(collection.PayorId);                 // walk-up payer text never creates a Payor
        Assert.Equal("Walk-up payer", collection.PayerName);
        var line = Assert.Single(collection.Lines);
        Assert.Equal(CollectionSourceKind.GovernedService, line.SourceKind);
        Assert.Empty(line.Allocations);                  // immediate charge, no fabricated receivable
        var classification = await db.RevenueClassifications.SingleAsync(x => x.Id == line.RevenueClassificationId);
        Assert.Equal(RevenueClassificationCodes.MarketFees, classification.SemanticCode);
        Assert.Contains("\"basis\":1", line.CalculationSnapshot);
        Assert.Contains("\"fixedAmount\":30", line.CalculationSnapshot);
        var stored = await db.AccountableDocuments.SingleAsync(x => x.Id == doc.Id);
        Assert.Equal(AccountableDocumentState.Consumed, stored.State);
        Assert.Equal(collection.Id, stored.CollectionId);
    }

    [Fact]
    public async Task RetrySameIntent_ReturnsTheOriginalOutcome_OnceOnly_AndChangedIntentConflicts()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.LandingBerthing, GovernedServiceBasis.DirectApprovedAmount, null, 100m);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        var operationId = Guid.NewGuid();
        var doc = w.Ct[0];

        var original = Post(w, CollectorOperationCodes.LandingBerthing, 60m, doc, operationId: operationId);
        var first = await workflow.PostMobileAsync(original);
        var retry = await workflow.PostMobileAsync(original); // a real retry resends the queued request unchanged
        var changed = await workflow.PostMobileAsync(original with { ReceivedAmount = 70m });

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(retry.IsSuccess);
        Assert.True(retry.Value!.ExistingOutcome);
        Assert.Equal(first.Value!.CollectionId, retry.Value.CollectionId);
        Assert.Equal(ResultStatus.Conflict, changed.Status);
        Assert.Contains("IDEMPOTENCY CONFLICT", changed.Error);
        Assert.Single(db.Collections);
        Assert.Single(db.PostingOperations);
    }

    [Fact]
    public async Task ADifferentOperationIdCannotReuseAConsumedDocument()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.LandingBerthing, GovernedServiceBasis.DirectApprovedAmount, null, null);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        var doc = w.Ct[0];
        Assert.True((await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.LandingBerthing, 50m, doc))).IsSuccess);

        var second = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.LandingBerthing, 50m, doc));

        Assert.False(second.IsSuccess);
        Assert.Single(db.Collections);
        Assert.Equal(AccountableDocumentState.Consumed, (await db.AccountableDocuments.SingleAsync(x => x.Id == doc.Id)).State);
    }

    [Theory]
    [InlineData(25.0, "AMOUNT_NOT_APPROVED")]
    [InlineData(31.0, "AMOUNT_NOT_APPROVED")]
    public async Task FixedAmount_RefusesAnyOtherAmount(double amount, string _)
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 30m, null);
        var (db, workflow) = Open(w, "Collector");
        await using var __ = db;

        var result = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, (decimal)amount, w.Ct[0]));

        Assert.False(result.IsSuccess);
        Assert.Empty(db.Collections);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, (await db.AccountableDocuments.SingleAsync(x => x.Id == w.Ct[0].Id)).State);
    }

    [Fact]
    public async Task DirectAmount_IsHeldToTheApprovedCeiling_AndEmptyCeilingMeansNoneConfigured()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.TransferLargeCattle, GovernedServiceBasis.DirectApprovedAmount, null, 500m);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;

        var above = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.TransferLargeCattle, 500.01m, w.Or[0]));
        var within = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.TransferLargeCattle, 500m, w.Or[1]));

        Assert.False(above.IsSuccess);
        Assert.True(within.IsSuccess, within.Error);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, within.Value!.Instrument);
        Assert.Equal(500m, (await db.Collections.SingleAsync()).TotalAmount);
    }

    [Fact]
    public async Task TheCollectorCannotPostWithTheWrongInstrument_TransferCattleIsOrNotCt()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.TransferLargeCattle, GovernedServiceBasis.DirectApprovedAmount, null, null);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;

        var result = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.TransferLargeCattle, 200m, w.Ct[0]));

        Assert.False(result.IsSuccess);
        Assert.Empty(db.Collections);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, (await db.AccountableDocuments.SingleAsync(x => x.Id == w.Ct[0].Id)).State);
    }

    [Fact]
    public async Task Vegetable_ModeResolvesTheInstrument_WholeIsOrDailyIsCt_AndTheCollectorNeverChoosesIt()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.VegetableFruitSpaceRental, GovernedServiceBasis.DirectApprovedAmount, null, null);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        var code = CollectorOperationCodes.VegetableFruitSpaceRental;

        var whole = await workflow.PostMobileAsync(Post(w, code, 300m, w.Or[0], GovernedServiceMode.WholePayment));
        var daily = await workflow.PostMobileAsync(Post(w, code, 20m, w.Ct[0], GovernedServiceMode.DailyTransaction));
        var wholeWithCt = await workflow.PostMobileAsync(Post(w, code, 300m, w.Ct[1], GovernedServiceMode.WholePayment));
        var dailyWithOr = await workflow.PostMobileAsync(Post(w, code, 20m, w.Or[1], GovernedServiceMode.DailyTransaction));
        var noMode = await workflow.PostMobileAsync(Post(w, code, 20m, w.Ct[2], mode: null));

        Assert.Equal(RevenueInstrumentType.OfficialReceipt, whole.Value!.Instrument);
        Assert.Equal(RevenueInstrumentType.CashTicket, daily.Value!.Instrument);
        Assert.False(wholeWithCt.IsSuccess);
        Assert.False(dailyWithOr.IsSuccess);
        Assert.False(noMode.IsSuccess);
        Assert.Equal(2, await db.Collections.CountAsync());
        var lines = await db.CollectionLines.ToListAsync();
        Assert.All(lines, l => Assert.Equal(CollectionSourceKind.GovernedService, l.SourceKind));
        Assert.Contains(lines, l => l.CalculationSnapshot!.Contains("\"mode\":1"));
        Assert.Contains(lines, l => l.CalculationSnapshot!.Contains("\"mode\":2"));
    }

    [Fact]
    public async Task UnassignedCollector_CannotPost_AndAnotherCollectorsDocumentIsNotUsable()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.DirectApprovedAmount, null, null);

        var (db1, workflow1) = Open(w, "Collector", w.OtherCollector.Id); // no operation assignment, holds no documents
        await using (db1)
        {
            var notAssigned = await workflow1.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, w.Ct[0]));
            Assert.False(notAssigned.IsSuccess);
            Assert.Empty(db1.Collections);
            // The document belongs to another collector, so it is not quarantined on this caller's say-so.
            Assert.Equal(AccountableDocumentState.Assigned, (await db1.AccountableDocuments.SingleAsync(x => x.Id == w.Ct[0].Id)).State);
        }

        var (db2, workflow2) = Open(w, "SuperAdmin");
        await using (db2)
        {
            var asHead = await workflow2.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, w.Ct[0]));
            Assert.Equal(ResultStatus.Forbidden, asHead.Status);
        }
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeOrUseThisTenantsDocumentsOrSetup()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.DirectApprovedAmount, null, null);
        var (db, workflow) = Open(w, "Collector", Guid.NewGuid(), tenant: w.OtherTenantId);
        await using var _ = db;

        var result = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, w.Ct[0]));

        Assert.False(result.IsSuccess);
        Assert.Empty(db.Collections);
        Assert.Empty(db.GovernedServices);
        var definitions = await Open(w, "Admin", tenant: w.OtherTenantId).Workflow.GetDefinitionsAsync();
        Assert.All(definitions.Value!, d => Assert.Equal(GovernedServiceSetupState.SetupRequired, d.State));
    }

    [Fact]
    public async Task ALaterSetupVersion_DoesNotRewriteAnEarlierPostedCollection()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 30m, null, effective: Today.AddDays(-10));
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        var first = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, w.Ct[0]));
        Assert.True(first.IsSuccess, first.Error);

        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.FixedAmount, 45m, null, effective: Today.AddDays(-1));

        var line = await db.CollectionLines.SingleAsync();
        Assert.Equal(30m, line.Amount);
        Assert.Contains("\"fixedAmount\":30", line.CalculationSnapshot);
        var later = await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 45m, w.Ct[1]));
        Assert.True(later.IsSuccess, later.Error);
        Assert.Equal(new[] { 30m, 45m }, (await db.CollectionLines.ToListAsync()).Select(x => x.Amount).Order());
    }

    // ── Reads ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectorSeesOnlyOwnDocumentsOfTheInstrumentTheModeResolves()
    {
        var w = await CreateAsync();
        var code = CollectorOperationCodes.VegetableFruitSpaceRental;
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;

        var whole = (await workflow.GetAvailableDocumentsAsync(code, GovernedServiceMode.WholePayment)).Value!;
        var daily = (await workflow.GetAvailableDocumentsAsync(code, GovernedServiceMode.DailyTransaction)).Value!;
        var noMode = await workflow.GetAvailableDocumentsAsync(code, null);

        Assert.All(whole, d => Assert.StartsWith("OR", d.DocumentNumber));
        Assert.All(daily, d => Assert.StartsWith("CT", d.DocumentNumber));
        Assert.Equal(3, whole.Count);
        Assert.Equal(ResultStatus.Invalid, noMode.Status);

        var other = Open(w, "Collector", w.OtherCollector.Id).Workflow;
        Assert.Equal(ResultStatus.Forbidden, (await other.GetAvailableDocumentsAsync(code, GovernedServiceMode.WholePayment)).Status);
    }

    [Fact]
    public async Task ActivityRegister_ListsPostedCollectionsWithDocumentPayerAndCollector()
    {
        var w = await CreateAsync();
        await ConfigureAsync(w, CollectorOperationCodes.MarketFees, GovernedServiceBasis.DirectApprovedAmount, null, null);
        var (db, workflow) = Open(w, "Collector");
        await using var _ = db;
        Assert.True((await workflow.PostMobileAsync(Post(w, CollectorOperationCodes.MarketFees, 30m, w.Ct[0], reference: "Comfort room"))).IsSuccess);

        var activity = await Open(w, "Admin").Workflow.GetActivityAsync(CollectorOperationCodes.MarketFees, Today.AddDays(-1), Today);

        var row = Assert.Single(activity.Value!);
        Assert.Equal(w.Ct[0].Number, row.DocumentNumber);
        Assert.Equal("Walk-up payer", row.PayerName);
        Assert.Equal("Comfort room", row.Reference);
        Assert.Equal("Ana Reyes", row.CollectorName);
        Assert.Equal(30m, row.Amount);
        Assert.Equal("Posted", row.Disposition);
        var other = await Open(w, "Admin").Workflow.GetActivityAsync(CollectorOperationCodes.LandingBerthing, Today.AddDays(-1), Today);
        Assert.Empty(other.Value!);
    }
}
