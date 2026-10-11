using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Vegetable / Fruit space rental on PostgreSQL (IA-046): the collector chooses the BUSINESS MODE, never the instrument. A monthly
/// rental resolves the Official Receipt policy and a daily transaction the Cash Ticket policy; each is one Collection with an SRC,
/// no typed serial, counted under the one Vegetable / Fruit income row. A disabled service writes nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VegetableFruitModesTests(PostgresFixture db)
{
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly Effective = new(2000, 1, 1);

    private sealed record World(Guid TenantId, Guid CollectorId);

    private sealed class Caller(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "mobile-collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "veg-modes";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private GovernedServiceWorkflow As(AppDbContext ctx, Guid tenantId, Guid userId, string role) =>
        new(ctx, new Caller(userId, tenantId, role), new FixedTenant(tenantId), new Clock());

    private async Task<World> SeedAsync(bool enabled = true)
    {
        var tenant = Municipality.Create($"vg-{Guid.NewGuid():N}"[..12], "Vegetable", "Province", MunicipalityStatus.Active,
            tenantCode: $"veg-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", $"vg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        await using var ctx = db.CreateContext(tenant.Id);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.VegetableFruitSpaceRental, tenant.Id);
        ctx.Add(classification);
        ctx.Add(RevenueClassificationPolicy.Create(classification.Id, Effective, "Vegetable / Fruit monthly rental",
            RevenueInstrumentType.OfficialReceipt, tenant.Id, businessContext: RevenuePolicyContext.VegetableWholePayment));
        ctx.Add(RevenueClassificationPolicy.Create(classification.Id, Effective, "Vegetable / Fruit daily transaction",
            RevenueInstrumentType.CashTicket, tenant.Id, businessContext: RevenuePolicyContext.VegetableDailyTransaction));
        var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.VegetableFruitSpaceRental, "head");
        ctx.Add(service);
        ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, Effective, GovernedServiceBasis.DirectApprovedAmount,
            null, 1000m, enabled, enabled, "head"));
        ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.VegetableFruitSpaceRental, "head"));
        await ctx.SaveChangesAsync();
        return new World(tenant.Id, collector.Id);
    }

    private static GovernedServicePostRequest Post(GovernedServiceMode mode, decimal amount, Guid? op = null) =>
        new(1, op ?? Guid.NewGuid(), CollectorOperationCodes.VegetableFruitSpaceRental, Today, amount, mode, "Pedro Vendor", null);

    private ObligationWorkflow Accounts(AppDbContext ctx, World w) =>
        new(ctx, new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new FixedTenant(w.TenantId), new Clock());

    private static CreateObligationAccountRequest Monthly(decimal amount = 900m) => new(
        ObligationKind.VegetableFruitSpaceRental, Guid.Empty, null, "", null, null,
        new DateOnly(Today.Year, Today.Month, 1), amount, OccupancyArrangement.SpaceOnly, null, "Pedro Vendor");

    private CollectionComposerWorkflow Collector(AppDbContext ctx, World w) =>
        new(ctx, new Caller(w.CollectorId, w.TenantId, "Collector"), new FixedTenant(w.TenantId), new Clock());

    [SkippableFact]
    public async Task Monthly_IsAnOfficialReceiptCollection_AndDaily_IsACashTicketCollection_EachWithAnSrcAndNoSerial()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = As(ctx, w.TenantId, w.CollectorId, "Collector");
            var account = await Accounts(ctx, w).CreateAccountAsync(Monthly());
            Assert.True(account.IsSuccess, account.Error);
            Assert.Equal("1", account.Value!.SubjectLabel);
            Assert.Equal(ObligationKind.VegetableFruitSpaceRental, account.Value.Kind);
            var monthly = await Collector(ctx, w).PostMobileObligationAsync(new(Guid.NewGuid(), account.Value.Id, Today.Year, Today.Month, 900m, Today));
            Assert.True(monthly.IsSuccess, monthly.Error);
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", monthly.Value!.ReferenceCode);
            var monthlyLine = await ctx.CollectionLines.SingleAsync();
            var policy = await ctx.RevenueClassificationPolicies.SingleAsync(x => x.Id == monthlyLine.RevenueClassificationPolicyId);
            Assert.Equal(RevenueInstrumentType.OfficialReceipt, policy.PermittedInstrumentType);
            Assert.Equal(RevenuePolicyContext.VegetableWholePayment, policy.BusinessContext);

            var daily = await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 50m));
            Assert.True(daily.IsSuccess, daily.Error);
            Assert.Equal(RevenueInstrumentType.CashTicket, daily.Value!.Instrument);
            Assert.NotEqual(monthly.Value.ReferenceCode, daily.Value.ReferenceCode);
        }
        await using var verify = db.CreateContext(w.TenantId);
        var collections = await verify.Collections.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, collections.Count);                                           // one Collection per transaction
        Assert.Equal(950m, collections.Sum(x => x.TotalAmount));
        Assert.All(collections, c => Assert.Equal(w.CollectorId, c.CollectorId));
        Assert.Empty(await verify.AccountableDocuments.ToListAsync());                 // no physical form, no typed serial
        var rows = await As(verify, w.TenantId, w.CollectorId, "Collector").GetCollectorRecordsAsync(Today, Today);
        Assert.Single(rows.Value!); // Daily activity stays transactional; Monthly lives in the account register.
        Assert.Single(await verify.ObligationPeriods.ToListAsync());
        var accountRow = Assert.Single((await Accounts(verify, w).GetAccountsAsync(ObligationKind.VegetableFruitSpaceRental)).Value!);
        Assert.Equal((900m, 900m, 0m), (accountRow.AssessedToDate, accountRow.CollectedToDate, accountRow.OutstandingToDate));
        var classificationId = (await verify.RevenueClassifications.SingleAsync()).Id;
        Assert.All(await verify.CollectionLines.ToListAsync(), line => Assert.Equal(classificationId, line.RevenueClassificationId));
        var report = (await new GetOfficialMonthlyIncomeQueryHandler(verify, new LegacyMonthlyIncomeReader(verify),
            new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new FixedTenant(w.TenantId), new Clock()).Handle(new(Today.Year, Today.Month), default)).Value!;
        var official = Assert.Single(report.Groups.SelectMany(x => x.Rows).Where(x => x.ClassificationCode == RevenueClassificationCodes.VegetableFruitSpaceRental));
        Assert.Equal(950m, official.Total.Canonical);
    }

    [SkippableFact]
    public async Task Monthly_SourceDiscoveryPartialSettlementAndClose_PreserveHistory()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync(); var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var accounts = Accounts(ctx, w);
        Assert.Equal(ResultStatus.Invalid, (await accounts.CreateAccountAsync(Monthly() with { SubjectLabel = "99" })).Status);
        var first = (await accounts.CreateAccountAsync(Monthly())).Value!;
        var second = (await accounts.CreateAccountAsync(Monthly())).Value!;
        Assert.Equal(("1", "2"), (first.SubjectLabel, second.SubjectLabel));
        var sources = new CollectionSessionSources(ctx, new Caller(w.CollectorId, w.TenantId, "Collector"), new FixedTenant(w.TenantId), new Clock(), null!, new NoMarketDays());
        var identity = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, first.Id);
        Assert.True(await sources.SourceExistsAsync(identity, default));
        Assert.Contains(await sources.SearchSourcesAsync("Pedro", default), x => x.Identity == identity && x.OperationCode == CollectorOperationCodes.VegetableFruitSpaceRental);
        var discovery = await sources.DiscoverNativeAsync(identity, Today, default);
        var choice = Assert.Single(Assert.Single(discovery.Operations).Choices!);
        Assert.Equal((CollectionSessionItemKind.Obligation, RevenueInstrumentType.OfficialReceipt, (decimal?)900m), (choice.Kind, choice.Instrument, choice.ServerAmount));
        Assert.True((await Collector(ctx, w).PostMobileObligationAsync(new(Guid.NewGuid(), first.Id, Today.Year, Today.Month, 300m, Today))).IsSuccess);
        var period = Assert.Single((await accounts.GetRegisterAsync(first.Id)).Value!);
        Assert.Equal((900m, 300m, 600m), (period.AssessedAmount, period.SettledAmount, period.OutstandingAmount));
        Assert.True((await accounts.CloseAccountAsync(first.Id, new(Today))).IsSuccess);
        Assert.Equal(600m, Assert.Single((await accounts.GetRegisterAsync(first.Id)).Value!).OutstandingAmount);
        Assert.Single(await ctx.Collections.ToListAsync());
        Assert.Single(await ctx.ObligationPeriods.ToListAsync());
        var rejected = await As(ctx, w.TenantId, w.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m));
        Assert.False(rejected.IsSuccess); Assert.Single(await ctx.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task MonthlyImport_UsesTenantScopedServerNumbers_AndDailyCreatesNoAssessment()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync(); var a = await SeedAsync(); var b = await SeedAsync();
        await using var ctx = db.CreateContext(a.TenantId);
        var request = new ImportSpaceHoldersRequest([new(Monthly()), new(Monthly() with { ActualOccupant = "Other holder" })]);
        var preview = await Accounts(ctx, a).PreviewSpaceHoldersAsync(request);
        var supplied = await Accounts(ctx, a).PreviewSpaceHoldersAsync(new([new(Monthly() with { SubjectLabel = "99" })]));
        Assert.False(supplied.Value!.CanSave);
        Assert.True(preview.Value!.CanSave);
        Assert.All(preview.Value.Rows, x => Assert.Equal(SpaceNumberOrigin.ServerSuggested, x.Facts!.NumberOrigin));
        Assert.Empty(await ctx.ObligationAccounts.ToListAsync());
        var imported = await Accounts(ctx, a).ImportSpaceHoldersAsync(request);
        Assert.Equal(2, imported.Value!.Imported);
        Assert.Equal(new[] { "1", "2" }, (await ctx.ObligationAccounts.OrderBy(x => x.SubjectLabel).ToListAsync()).Select(x => x.SubjectLabel));
        Assert.True((await As(ctx, a.TenantId, a.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 50m))).IsSuccess);
        Assert.Empty(await ctx.ObligationPeriods.ToListAsync());
        await using var other = db.CreateContext(b.TenantId);
        Assert.Equal("1", (await Accounts(other, b).CreateAccountAsync(Monthly())).Value!.SubjectLabel);
        Assert.Equal(ResultStatus.NotFound, (await Accounts(other, b).GetRegisterAsync((await ctx.ObligationAccounts.FirstAsync()).Id)).Status);
    }

    private sealed class NoMarketDays : ITpmMarketDayProvider
    {
        public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    [SkippableFact]
    public async Task HistoricalWholeReplay_RemainsReadable_AndCannotBecomeASecondMonthlyWriter()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync(); var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var request = Post(GovernedServiceMode.WholePayment, 100m);
        var classification = await ctx.RevenueClassifications.SingleAsync();
        var policy = await ctx.RevenueClassificationPolicies.SingleAsync(x => x.BusinessContext == RevenuePolicyContext.VegetableWholePayment);
        var service = await ctx.GovernedServices.SingleAsync();
        // Frozen pre-transition evidence, created directly in the test fixture rather than enabling an obsolete writer.
        var normalized = System.Text.Json.JsonSerializer.Serialize(new {
            request.SchemaVersion, OperationType = "GovernedServiceCollection", TenantId = w.TenantId,
            ActorId = w.CollectorId.ToString("N"), request.OperationCode, Mode = "WholePayment",
            BusinessDate = Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ReceivedAmount = "100.00",
            request.PayerName, request.Reference
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var snapshot = System.Text.Json.JsonSerializer.Serialize(new {
            SchemaVersion = 1, OperationCode = request.OperationCode, Instrument = RevenueInstrumentType.OfficialReceipt,
            Mode = GovernedServiceMode.WholePayment, ClassificationCode = classification.SemanticCode
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var historical = await new CanonicalCollectionPostingCoordinator(ctx).PostAsync(w.TenantId, request.ClientOperationId, 1,
            normalized, "MobileGovernedService", w.CollectorId.ToString("N"), "mobile-collector", "Collector", Today, "test",
            [new(classification, policy, 100m, CollectionSourceKind.GovernedService, service.Id, null, snapshot, null)],
            collectorId: w.CollectorId, payerName: request.PayerName);
        var workflow = As(ctx, w.TenantId, w.CollectorId, "Collector");
        var retry = await workflow.PostMobileAsync(request);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal((historical.Id, historical.ReferenceCode, true), (retry.Value!.CollectionId, retry.Value.ReferenceCode, retry.Value.ExistingOutcome));
        Assert.False((await workflow.PostMobileAsync(request with { ClientOperationId = Guid.NewGuid() })).IsSuccess);
        var account = (await Accounts(ctx, w).CreateAccountAsync(Monthly())).Value!;
        Assert.True((await Collector(ctx, w).PostMobileObligationAsync(new(Guid.NewGuid(), account.Id, Today.Year, Today.Month, 300m, Today))).IsSuccess);
        Assert.True((await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 50m))).IsSuccess);
        var report = (await new GetOfficialMonthlyIncomeQueryHandler(ctx, new LegacyMonthlyIncomeReader(ctx),
            new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new FixedTenant(w.TenantId), new Clock()).Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(450m, report.Groups.SelectMany(x => x.Rows).Single(x => x.ClassificationCode == RevenueClassificationCodes.VegetableFruitSpaceRental).Total.Canonical);
        Assert.Equal(3, await ctx.Collections.CountAsync());
        Assert.Equal(600m, Assert.Single((await Accounts(ctx, w).GetRegisterAsync(account.Id)).Value!).OutstandingAmount);
    }

    [SkippableFact]
    public async Task Replay_ReturnsTheSameCollectionAndSrc_AndADifferentIntentOnTheSameIdConflicts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var op = Guid.NewGuid();
        string src;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var first = await As(ctx, w.TenantId, w.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 900m, op));
            Assert.True(first.IsSuccess, first.Error);
            src = first.Value!.ReferenceCode;
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = As(ctx, w.TenantId, w.CollectorId, "Collector");
            var replay = await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 900m, op));
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((src, true), (replay.Value!.ReferenceCode, replay.Value.ExistingOutcome));
            var conflict = await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 50m, op));
            Assert.False(conflict.IsSuccess);
            Assert.Equal(1, await ctx.Collections.CountAsync());
        }
    }

    [SkippableFact]
    public async Task ADisabledService_AnOverCeilingAmount_AnUnassignedCollector_AndAnotherTenant_WriteNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var off = await SeedAsync(enabled: false);
        await using (var ctx = db.CreateContext(off.TenantId))
        {
            Assert.False((await As(ctx, off.TenantId, off.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 900m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }

        await db.ResetAsync();
        var a = await SeedAsync();
        var b = await SeedAsync();
        await using (var ctx = db.CreateContext(a.TenantId))
        {
            var workflow = As(ctx, a.TenantId, a.CollectorId, "Collector");
            Assert.False((await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 1000.01m))).IsSuccess);          // ceiling
            Assert.False((await As(ctx, a.TenantId, Guid.NewGuid(), "Collector")
                .PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 900m))).IsSuccess);                                   // unassigned
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        await using (var ctx = db.CreateContext(b.TenantId))   // tenant A's collector acting inside tenant B
        {
            Assert.False((await As(ctx, b.TenantId, a.CollectorId, "Collector")
                .PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 900m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
    }

    [SkippableFact]
    public async Task TurningTheServiceOn_AppendsAVersion_AndNeverRewritesThePolicyOrAnEarlierCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await As(ctx, w.TenantId, w.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 900m))).IsSuccess);

        int policies;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            policies = await ctx.RevenueClassificationPolicies.CountAsync();
            var head = As(ctx, w.TenantId, Guid.NewGuid(), "SuperAdmin");
            // Enabling reuses the existing policy versions (no duplicate classification/date row is ever needed for it).
            var saved = await head.ConfigureAsync(CollectorOperationCodes.VegetableFruitSpaceRental,
                new ConfigureGovernedServiceRequest(Today, GovernedServiceBasis.DirectApprovedAmount, null, 1200m, true, true));
            Assert.True(saved.IsSuccess, saved.Error);
        }
        await using var verify = db.CreateContext(w.TenantId);
        Assert.Equal(policies, await verify.RevenueClassificationPolicies.CountAsync());           // no policy rows added or changed
        Assert.Equal(2, await verify.GovernedServiceSettings.CountAsync());                        // history kept; the new version appended
        Assert.Equal(900m, (await verify.Collections.SingleAsync()).TotalAmount);                  // the earlier Collection is untouched
    }
}
