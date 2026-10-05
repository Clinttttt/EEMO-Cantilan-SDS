using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CollectionSessionTests(PostgresFixture database)
{
    private static readonly DateOnly Today = PhilippineTime.Today;
    private sealed record Tenant(Guid MunicipalityId) : ICurrentMunicipalityAccessor { public void Set(Guid id) { } }
    private sealed class NoMarketDays : ITpmMarketDayProvider
    {
        public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default) => throw new InvalidOperationException("Tabo is not an itemized capability.");
        public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default) => throw new InvalidOperationException("Tabo is not an itemized capability.");
    }
    private sealed class Clock(DateOnly? businessDate = null) : IClock
    {
        public DateTime UtcNow => businessDate?.ToDateTime(new TimeOnly(4, 0), DateTimeKind.Utc) ?? DateTime.UtcNow;
        public DateTime PhilippineNow => businessDate?.ToDateTime(new TimeOnly(12, 0)) ?? PhilippineTime.Now;
        public DateOnly PhilippineToday => businessDate ?? Today;
    }
    private sealed record Caller(Guid Id, Guid TenantId, string Role = "Collector") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Id;
        public Guid? CollectorId => Role == "Collector" ? Id : null;
        public Guid? MunicipalityId => TenantId;
        public string? Username => "session-test";
        public string? MunicipalityCode => "session-test";
        public AdminUserDto? GetCurrentUser() => null;
    }
    private sealed record World(Guid TenantId, Guid CollectorId, Guid PayorId, Guid AccountId, Guid StallId);
    private async Task<World> SeedAsync(DateOnly? accountStart = null)
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "");
        await database.ResetAsync();
        var tenant = Municipality.Create("SESSION-" + Guid.NewGuid().ToString("N")[..8], "Session test", "Province", MunicipalityStatus.Active,
            tenantCode: "session-" + Guid.NewGuid().ToString("N")[..8]);
        await using (var setup = database.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var db = database.CreateContext(tenant.Id);
        var collector = CollectorUser.Create("Session Collector", "C-1", "session-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new HashedPassword("test"), tenant.Id);
        var facility = Facility.Create(FacilityCode.NPM, "Market", "NPM", archetype: BillingArchetype.DailyStall, municipalityId: tenant.Id);
        var stall = Stall.Create(facility.Id, "FISH-1", 900m, ApplicableFees.BaseRental | ApplicableFees.Water | ApplicableFees.Electricity,
            MarketSection.FishSection, createdBy: "test", municipalityId: tenant.Id);
        var payor = Payor.Create(tenant.Id, "One Payer", BusinessPayorKind.Person, "test");
        var contract = Contract.Create(stall.Id, "One Payer", "One Payer", (accountStart ?? Today).AddYears(-1), 20, 900m, createdBy: "test");
        contract.AssociatePayor(payor.Id, "test");
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
        db.AddRange(collector, facility, stall, payor, contract);
        foreach (var (code, classificationCode, fixedAmount) in new[]
        {
            (CollectorOperationCodes.MarketFees, RevenueClassificationCodes.MarketFees, 30m),
            (CollectorOperationCodes.LandingBerthing, RevenueClassificationCodes.LandingBerthing, 200m)
        })
        {
            var classification = RevenueClassification.Create(classificationCode, tenant.Id);
            var service = GovernedService.Create(tenant.Id, code, "head");
            db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, new(2000, 1, 1), code, RevenueInstrumentType.CashTicket, tenant.Id),
                service, GovernedServiceSetting.Create(tenant.Id, service.Id, Today.AddDays(-1), GovernedServiceBasis.FixedAmount, fixedAmount, null, true, true, "head"),
                CollectorOperationAssignment.Assign(tenant.Id, collector.Id, code, "head"));
        }
        var fish = RevenueClassification.Create(RevenueClassificationCodes.FishMeatVendorFee, tenant.Id);
        var water = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenant.Id);
        var electricity = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant.Id);
        var startDate = accountStart ?? Today;
        var start = new DateOnly(startDate.Year, startDate.Month, 1);
        var account = ObligationAccount.Create(tenant.Id, ObligationKind.FishMeatVendorFee, payor.Id, stall.Id, "Fish vendor", null, null, start, "head");
        db.AddRange(fish, water, electricity,
            RevenueClassificationPolicy.Create(fish.Id, new(2000, 1, 1), "Vendor Fee", RevenueInstrumentType.OfficialReceipt, tenant.Id),
            RevenueClassificationPolicy.Create(water.Id, new(2000, 1, 1), "Water", RevenueInstrumentType.CashTicket, tenant.Id),
            RevenueClassificationPolicy.Create(electricity.Id, new(2000, 1, 1), "Electricity", RevenueInstrumentType.OfficialReceipt, tenant.Id),
            account, ObligationRate.Create(tenant.Id, account.Id, start, 900m, "head"));
        await db.SaveChangesAsync();
        return new(tenant.Id, collector.Id, payor.Id, account.Id, stall.Id);
    }
    private static CollectionSessionIntent Basket(World w, Guid? payor = null) => new(Guid.NewGuid(), Today, payor ?? w.PayorId,
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), CollectionSessionItemKind.GovernedService, 30m, Service: new(CollectorOperationCodes.MarketFees)),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), CollectionSessionItemKind.GovernedService, 200m, Service: new(CollectorOperationCodes.LandingBerthing)),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), CollectionSessionItemKind.VendorFee, 900m,
            VendorFee: new(w.StallId))
    ]);
    private static CollectionSessionWorkflow Workflow(AppDbContext db, World w, ICollectionSessionSources? sources = null, Caller? actor = null) =>
        new(new CollectionSessionStore(db), sources ?? new CollectionSessionSources(db, actor ?? new(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays()),
            actor ?? new(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), NullLogger<CollectionSessionWorkflow>.Instance);
    private static async Task<CollectionSessionResult> Record(CollectionSessionWorkflow flow, CollectionSessionIntent intent)
    {
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(p => p.Message)));
        var result = await flow.RecordAsync(new(intent, quote.QuoteFingerprint));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private sealed class Code : ITenantContext { public string TenantCode => "session-test"; }
    private sealed class NoCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static NpmWholePaymentWorkflow Whole(AppDbContext db, World w)
    {
        var caller = new Caller(w.CollectorId, w.TenantId); var tenant = new Tenant(w.TenantId);
        return new(new StallRepository(db), new CollectorRepository(db), new DailyCollectionRepository(db),
            new NpmMonthSettlementService(new DailyCollectionRepository(db), new NpmMarketClosureRepository(db), new FeeRateResolver(db), new Clock()),
            new NpmDailyCanonicalPoster(db, caller, tenant, new GovernedCanonicalAuthority(db, tenant)), caller, new Clock(), new NoCache(), new Code());
    }
    private static async Task EnableRentAndWeighing(AppDbContext db, World w)
    {
        var rent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, w.TenantId);
        var weighing = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, w.TenantId);
        var service = GovernedService.Create(w.TenantId, CollectorOperationCodes.NpmDaily, "head");
        db.AddRange(rent, weighing, service,
            RevenueClassificationPolicy.Create(rent.Id, Today, "Rent", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            RevenueClassificationPolicy.Create(weighing.Id, Today, "Weight & Measure", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "head"),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Today.AddYears(-2), w.TenantId),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 3m, Today, w.TenantId));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Direct_vendor_fee_is_append_only_replays_and_never_creates_an_assessment_or_changes_rent()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        await EnableRentAndWeighing(db, w);
        var whole = Whole(db, w);
        Assert.Equal(900m, (await whole.QuoteAsync(w.StallId, Today.Year, Today.Month)).Value!.Amount);
        var vendor = new FishMeatVendorFeeCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock());
        var request = new DirectVendorFeeRequest(Guid.NewGuid(), Today, w.StallId, w.PayorId, 100m);
        var first = (await vendor.PostAsync(request)).Value!;
        Assert.StartsWith("SRC-", first.ReferenceCode);
        Assert.Equal(first.CollectionId, (await vendor.PostAsync(request)).Value!.CollectionId);
        Assert.Equal(ResultStatus.Conflict, (await vendor.PostAsync(request with { AmountReceived = 101m })).Status);
        Assert.True((await vendor.PostAsync(request with { ClientOperationId = Guid.NewGuid(), AmountReceived = 50m })).IsSuccess);
        Assert.Equal(150m, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(2, await db.Collections.CountAsync()); Assert.Empty(await db.ObligationPeriods.ToListAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync());
        Assert.Equal(900m, (await whole.QuoteAsync(w.StallId, Today.Year, Today.Month)).Value!.Amount);
        Assert.All(await db.CollectionLines.ToListAsync(), x => Assert.Equal(CollectionSourceKind.FishMeatVendorFee, x.SourceKind));
    }

    [SkippableFact]
    public async Task Direct_vendor_fee_preserves_pre_cutover_obligation_money_without_allocating_to_it()
    {
        var historicalDate = FishMeatVendorFeeRules.DirectEffectiveDate.AddDays(-1);
        var w = await SeedAsync(historicalDate); await using var db = database.CreateContext(w.TenantId);
        var historical = new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock(historicalDate));
        var oldRequest = new MobileObligationPostRequest(Guid.NewGuid(), w.AccountId,
            historicalDate.Year, historicalDate.Month, 50m, historicalDate);
        var posted = await historical.PostMobileObligationAsync(oldRequest);
        Assert.True(posted.IsSuccess, posted.Error);
        var oldCollection = Assert.Single(await db.Collections.AsNoTracking().ToListAsync());
        var period = Assert.Single(await db.ObligationPeriods.AsNoTracking().ToListAsync());
        var allocations = await db.CollectionAllocations.CountAsync();
        var direct = new FishMeatVendorFeeCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock());
        Assert.True((await direct.PostAsync(new(Guid.NewGuid(), Today, w.StallId, w.PayorId, 100m))).IsSuccess);
        Assert.Equal(2, await db.Collections.CountAsync());
        Assert.Equal(150m, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(allocations, await db.CollectionAllocations.CountAsync());
        Assert.Equal(period.SettlementVersion, (await db.ObligationPeriods.AsNoTracking().SingleAsync()).SettlementVersion);
        Assert.Equal(oldCollection.ReferenceCode, (await db.Collections.AsNoTracking().SingleAsync(x => x.Id == oldCollection.Id)).ReferenceCode);
        var accounts = await db.ObligationAccounts.AsNoTracking().ToListAsync();
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(w.TenantId, accounts, Today, default);
        Assert.All(quotes, x => Assert.False(x.CanAddToDraft));
        Assert.Single(await db.ObligationPeriods.ToListAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Itemized_rent_vendor_fee_and_weighing_keep_three_classifications_and_atomic_retries(bool injectFailure)
    {
        var w = await SeedAsync();
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.Parse("00000000-0000-0000-0000-000000000001"), CollectionSessionItemKind.NpmWholePayment, 0m, NpmWhole: new(w.StallId, Today.Year, Today.Month)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000002"), CollectionSessionItemKind.VendorFee, 100m, VendorFee: new(w.StallId)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000003"), CollectionSessionItemKind.Weighing, 0m, Weighing: new(w.StallId, WeighingType.Fish, 22m))]);
        await using (var setup = database.CreateContext(w.TenantId)) await EnableRentAndWeighing(setup, w);
        if (injectFailure)
        {
            await using var failing = database.CreateContext(w.TenantId);
            var sources = new CollectionSessionSources(failing, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays(), Whole(failing, w));
            var flow = Workflow(failing, w, new FailAfterFirst(sources));
            var quote = (await flow.QuoteAsync(intent)).Value!;
            Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(x => x.Message)));
            await Assert.ThrowsAsync<IOException>(() => flow.RecordAsync(new(intent, quote.QuoteFingerprint)));
        }
        await using var db = database.CreateContext(w.TenantId);
        Assert.Equal(0, await db.Collections.CountAsync()); Assert.Equal(0, await db.DailyCollections.CountAsync());
        var whole = Whole(db, w);
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays(), whole);
        var workflow = Workflow(db, w, source);
        var result = await Record(workflow, intent);
        Assert.Equal(1066m, result.GrandTotal); Assert.Equal(3, result.Collections.Count);
        Assert.Equal(3, result.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.Equal(0m, (await whole.QuoteAsync(w.StallId, Today.Year, Today.Month)).Value!.Amount);
        Assert.Empty(await db.ObligationPeriods.ToListAsync());
        var replay = (await workflow.RecordAsync(new(intent, "already recorded"))).Value!;
        Assert.Equal(result.Collections.Select(x => x.CollectionId), replay.Collections.Select(x => x.CollectionId));
        Assert.Equal(3, await db.Collections.CountAsync());
        var income = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), new Caller(Guid.NewGuid(), w.TenantId, "Admin"), new Tenant(w.TenantId), new Clock());
        var before = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        var rows = before.Groups.SelectMany(x => x.Rows).ToArray();
        Assert.Equal(900m, rows.Single(x => x.Key == "RENT_NPM").Total.Total);
        Assert.Equal(100m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.FishMeatVendorFee).Total.Total);
        Assert.Equal(66m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.WeightAndMeasure).Total.Total);
        var register = (await new CollectionsReportWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId)).GetMyRegisterAsync(Today, Today)).Value!;
        Assert.Equal(1066m, register.Net); Assert.Equal(3, register.Rows.Count);
        Assert.Equal(3, (await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today)).Count);
        var report = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(1066m, report.Lines.Sum(x => x.Amount) + (report.OperationCollections?.Sum(x => x.Amount) ?? 0));
        var remittance = new RemittanceWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "Admin"), new Tenant(w.TenantId));
        Assert.Equal(3, (await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections.Count);
        Assert.True((await remittance.RecordAsync(new(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null, result.Collections.Select(x => x.CollectionId).ToArray(), 1066m, null, null))).IsSuccess);
        Assert.Equal(before.GrandTotal.Total, (await income.Handle(new(Today.Year, Today.Month), default)).Value!.GrandTotal.Total);
    }

    [SkippableFact]
    public async Task Mixed_instruments_keep_three_source_boundaries_and_reporting_and_remittance_count_each_once()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w); var intent = Basket(w);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.Equal(1130m, quote.GrandTotal);
        Assert.Equal(3, quote.Items.Select(x => x.GroupId).Distinct().Count());
        Assert.Equal(230m, quote.InstrumentTotals.Single(x => x.Instrument == RevenueInstrumentType.CashTicket).Amount);
        Assert.Equal(900m, quote.InstrumentTotals.Single(x => x.Instrument == RevenueInstrumentType.OfficialReceipt).Amount);
        Assert.Equal(0, await db.Collections.CountAsync()); Assert.Equal(0, await db.ObligationPeriods.CountAsync());
        var posted = await Record(flow, intent);
        Assert.Equal(CollectionSessionStatus.Recorded, posted.Status);
        Assert.Equal(3, posted.Collections.Count); Assert.Equal(3, posted.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(posted.Collections, c => Assert.Matches("^SRC-", c.ReferenceCode));
        Assert.Equal(3, await db.CollectionLines.CountAsync()); Assert.Equal(3, await db.PostingOperations.CountAsync());
        Assert.Single(await db.MobileCollectionSessions.ToListAsync());
        var replay = (await flow.RecordAsync(new(intent with { Items = intent.Items.Reverse().ToArray() }, quote.QuoteFingerprint))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(posted.Collections.Select(c => c.CollectionId), replay.Collections.Select(c => c.CollectionId));
        Assert.Equal(posted.Collections.Select(c => c.ReferenceCode), replay.Collections.Select(c => c.ReferenceCode));
        Assert.Equal(3, await db.Collections.CountAsync()); Assert.Equal(3, await db.CollectionLines.CountAsync());

        var collector = new Caller(w.CollectorId, w.TenantId); var head = new Caller(Guid.NewGuid(), w.TenantId, "Admin");
        var tenant = new Tenant(w.TenantId);
        var activity = await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today);
        Assert.Equal(3, activity.Count); Assert.Equal(1130m, activity.Sum(x => x.Amount));
        var register = (await new CollectionsReportWorkflow(db, collector, tenant).GetMyRegisterAsync(Today, Today)).Value!;
        Assert.Equal(3, register.Rows.Count); Assert.Equal(1130m, register.Rows.Sum(x => x.Amount));
        var report = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(1130m, report.Lines.Sum(x => x.Amount) + (report.OperationCollections?.Sum(x => x.Amount) ?? 0));
        var records = (await new GovernedServiceWorkflow(db, collector, tenant, new Clock()).GetCollectorRecordsAsync(Today, Today)).Value!;
        Assert.Equal(2, records.Count); Assert.Equal(230m, records.Sum(x => x.Amount));
        var income = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, tenant, new Clock());
        var before = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(1130m, before.GrandTotal.Total);
        var rows = before.Groups.SelectMany(x => x.Rows).ToList();
        Assert.Equal(30m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.MarketFees).Total.Total);
        Assert.Equal(200m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.LandingBerthing).Total.Total);
        Assert.Equal(900m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.FishMeatVendorFee).Total.Total);
        var remit = new RemittanceWorkflow(db, head, tenant);
        var scope = (await remit.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!;
        Assert.Equal(3, scope.Collections.Count);
        var recorded = await remit.RecordAsync(new(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            posted.Collections.Select(x => x.CollectionId).ToArray(), 1130m, null, null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var position = Assert.Single((await remit.GetPositionAsync(Today, Today)).Value!.Collectors);
        Assert.Equal((1130m, 1130m, 0m), (position.Collected, position.Remitted, position.Unremitted));
        var after = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(JsonSerializer.Serialize(before.Groups), JsonSerializer.Serialize(after.Groups));
        Assert.Equal(3, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Payer_mismatch_and_optional_payer_and_changed_intent_are_enforced_before_money()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w);
        var other = Payor.Create(w.TenantId, "One Payer", BusinessPayorKind.Person, "test"); db.Add(other); await db.SaveChangesAsync();
        var invalid = (await flow.QuoteAsync(Basket(w, other.Id))).Value!;
        Assert.Contains(invalid.Problems, p => p.Code == "PayerMismatch"); Assert.Equal(0, await db.Collections.CountAsync());
        var basket = Basket(w); var anonymous = basket with { PayorId = null, Items = basket.Items.Take(2).ToArray() };
        var posted = await Record(flow, anonymous); Assert.Equal(230m, posted.GrandTotal);
        var missing = (await flow.QuoteAsync(basket with { PayorId = null })).Value!;
        Assert.False(missing.CanRecord);
        var conflict = (await flow.RecordAsync(new(anonymous with { PayorId = w.PayorId }, "ignored"))).Value!;
        Assert.Contains(conflict.Problems, p => p.Code == "SessionIntentConflict"); Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Stale_policy_and_disabled_policy_preflight_leave_no_collection_or_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w); var basket = Basket(w);
        var quote = (await flow.QuoteAsync(basket)).Value!;
        var fish = await db.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee);
        db.Add(RevenueClassificationPolicy.Create(fish.Id, Today, "Vendor Fee", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        await db.SaveChangesAsync();
        var stale = (await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.NeedsReview, stale.Status); Assert.Empty(await db.Collections.ToListAsync());
        var landing = await db.GovernedServices.SingleAsync(x => x.OperationCode == CollectorOperationCodes.LandingBerthing);
        db.Add(GovernedServiceSetting.Create(w.TenantId, landing.Id, Today, GovernedServiceBasis.FixedAmount, 200m, null, false, true, "head"));
        await db.SaveChangesAsync();
        var disabled = (await flow.QuoteAsync(basket)).Value!;
        Assert.Contains(disabled.Problems, p => p.ClientItemId == basket.Items[1].ClientItemId && p.Code == "SourceNotAvailable");
        Assert.Empty(await db.Collections.ToListAsync());
    }

    private sealed class FailAfterFirst(ICollectionSessionSources inner) : ICollectionSessionSources
    {
        private int _count;
        public Task<CollectionSessionDiscovery> DiscoverAsync(Guid? id, DateOnly date, CancellationToken ct) => inner.DiscoverAsync(id, date, ct);
        public Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteAsync(CollectionSessionIntent s, CollectionSessionItemIntent i, CancellationToken ct) => inner.QuoteAsync(s, i, ct);
        public async Task<CollectionSessionCollection> PostAsync(CollectionSessionIntent s, CollectionSessionItemIntent i, Guid id, CancellationToken ct)
        {
            if (++_count == 2) throw new IOException("Injected outage after first child save");
            return await inner.PostAsync(s, i, id, ct);
        }
    }
    [SkippableFact]
    public async Task A_failure_after_a_child_save_rolls_back_everything_and_same_session_retries_safely()
    {
        var w = await SeedAsync(); var basket = Basket(w);
        await using (var db = database.CreateContext(w.TenantId))
        {
            var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays());
            var flow = Workflow(db, w, new FailAfterFirst(sources)); var quote = (await flow.QuoteAsync(basket)).Value!;
            await Assert.ThrowsAsync<IOException>(() => flow.RecordAsync(new(basket, quote.QuoteFingerprint)));
        }
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Equal(0, await verify.Collections.CountAsync()); Assert.Equal(0, await verify.PostingOperations.CountAsync());
        Assert.Equal(0, await verify.MobileCollectionSessions.CountAsync()); Assert.Equal(0, await verify.ObligationPeriods.CountAsync());
        Assert.Equal(3, (await Record(Workflow(verify, w), basket)).Collections.Count);
    }

    [SkippableFact]
    public async Task Tenant_role_assignment_and_result_ownership_fail_closed()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var basket = Basket(w);
        Assert.Equal(ResultStatus.Forbidden, (await Workflow(db, w, actor: new(w.CollectorId, w.TenantId, "Admin")).QuoteAsync(basket)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Workflow(db, w, actor: new(w.CollectorId, Guid.NewGuid())).QuoteAsync(basket)).Status);
        var invalidPayor = (await Workflow(db, w).QuoteAsync(basket with { PayorId = Guid.NewGuid() })).Value!;
        Assert.Contains(invalidPayor.Problems, p => p.Code == "InvalidPayor");
        var assignments = await db.CollectorOperationAssignments.ToListAsync(); db.RemoveRange(assignments); await db.SaveChangesAsync();
        Assert.False((await Workflow(db, w).QuoteAsync(basket)).Value!.CanRecord);
        Assert.Equal(0, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Prepared_water_wins_and_canonical_electricity_uses_existing_bill_writer()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        var bill = UtilityBill.Create(w.StallId, Today.Year, Today.Month, 0, 8, 10, 0, 1, 83, "head");
        db.Add(bill); await db.SaveChangesAsync();
        var waterSource = Assert.Single((await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock())
            .GetMobileSourcesAsync(Today.Year, Today.Month)).Value!);
        var item = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Water, 70m,
            Water: new(w.StallId, Today.Year, Today.Month, bill.Id, waterSource.WaterSourceVersion));
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [item]);
        var flow = Workflow(db, w);
        var changed = (await flow.QuoteAsync(basket)).Value!;
        Assert.Equal(70m, Assert.Single(changed.Items).Amount);
        Assert.True(changed.CanRecord);
        Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(83m, (await Record(flow, basket with { Items = [item with { ConfirmedAmount = 83m }] })).GrandTotal);

        var electricity = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Electricity, 80m,
            Electricity: new(bill.Id, bill.ElectricitySourceVersion));
        var ecf = basket with { ClientCollectionSessionId = Guid.NewGuid(), Items = [electricity] };
        Assert.True((await flow.QuoteAsync(ecf)).Value!.CanRecord);
        bill.MarkElectricityPendingCutover();
        var at = DateTime.UtcNow.AddMinutes(-2);
        var cutover = CollectionSettlementCutover.Freeze(w.TenantId, CollectionSourceKind.UtilityBill, bill.Id,
            CollectionSourcePart.Electricity, bill.ElectricitySourceVersion, at, 80m, 0m, 80m,
            "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}", Guid.NewGuid(), at.AddMinutes(1));
        bill.ActivateCanonicalElectricitySettlement(cutover); db.Add(cutover); await db.SaveChangesAsync();
        var recorded = await Record(flow, ecf with { Items = [electricity with { Electricity = new(bill.Id, bill.ElectricitySourceVersion) }] });
        Assert.Equal(80m, recorded.GrandTotal); Assert.Equal(RevenueInstrumentType.OfficialReceipt, Assert.Single(recorded.Collections).Instrument);
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Dynamic_fee_identity_and_effective_rate_version_are_revalidated()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var service = await db.GovernedServices.SingleAsync(x => x.OperationCode == CollectorOperationCodes.MarketFees);
        db.Add(GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.ApprovedFeeOption, null, null, true, true, "head"));
        var option = GovernedServiceFeeOption.Create(w.TenantId, service.Id, "Configured facility charge", null, "Terminal", null, "head");
        db.AddRange(option, GovernedServiceFeeOptionRate.Create(w.TenantId, option.Id, Today.AddDays(-1), GovernedServiceBasis.FixedAmount, 30m, null, "head"));
        await db.SaveChangesAsync();
        var basket = Basket(w); basket = basket with { Items = [basket.Items[0] with { Service = new(CollectorOperationCodes.MarketFees, FeeOptionId: option.Id) }] };
        var flow = Workflow(db, w); var quote = (await flow.QuoteAsync(basket)).Value!; Assert.True(quote.CanRecord);
        var missing = (await flow.QuoteAsync(basket with { Items = [basket.Items[0] with { Service = new(CollectorOperationCodes.MarketFees) }] })).Value!;
        Assert.Contains(missing.Problems, p => p.Code == "InvalidSource");
        // Same amount, different approved effective version: the old quote must still be stale.
        db.Add(GovernedServiceFeeOptionRate.Create(w.TenantId, option.Id, Today, GovernedServiceBasis.FixedAmount, 30m, null, "head"));
        await db.SaveChangesAsync();
        var stale = (await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!;
        Assert.Contains(stale.Problems, p => p.Code == "QuoteStale"); Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(30m, (await Record(flow, basket)).GrandTotal);
    }

    [SkippableFact]
    public async Task Foreign_payer_account_and_source_are_unavailable_without_cross_tenant_details()
    {
        var w = await SeedAsync();
        var foreign = Municipality.Create("FOREIGN-" + Guid.NewGuid().ToString("N")[..6], "Foreign", "Province", MunicipalityStatus.Active,
            tenantCode: "foreign-" + Guid.NewGuid().ToString("N")[..8]);
        await using (var setup = database.CreateContext(Guid.Empty)) { setup.Add(foreign); await setup.SaveChangesAsync(); }
        var payer = Payor.Create(foreign.Id, "Secret payer", BusinessPayorKind.Person, "test");
        var facility = Facility.Create(FacilityCode.NPM, "Foreign market", "NPM", municipalityId: foreign.Id);
        var stall = Stall.Create(facility.Id, "SECRET-1", 0m, ApplicableFees.Water, MarketSection.FishSection, createdBy: "test", municipalityId: foreign.Id);
        var account = ObligationAccount.Create(foreign.Id, ObligationKind.FishMeatVendorFee, payer.Id, stall.Id, "Secret account", null, null, Today, "test");
        var bill = UtilityBill.Create(stall.Id, Today.Year, Today.Month, 0, 8, 10, 0, 1, 83, "head");
        await using (var other = database.CreateContext(foreign.Id)) { other.AddRange(payer, facility, stall, account, bill); await other.SaveChangesAsync(); }
        await using var db = database.CreateContext(w.TenantId); var flow = Workflow(db, w); var basket = Basket(w);
        Assert.Contains((await flow.QuoteAsync(basket with { PayorId = payer.Id })).Value!.Problems, p => p.Code == "InvalidPayor");
        var invalid = (await flow.QuoteAsync(basket with { Items = [basket.Items[2] with { VendorFee = new(stall.Id) }] })).Value!;
        Assert.Contains(invalid.Problems, p => p.Code == "InvalidSource");
        Assert.DoesNotContain(invalid.Problems, p => p.Message.Contains("Secret"));
        var foreignBill = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Electricity, 80m, Electricity: new(bill.Id, bill.ElectricitySourceVersion));
        Assert.Contains((await flow.QuoteAsync(basket with { Items = [foreignBill] })).Value!.Problems, p => p.Code == "InvalidSource");
        Assert.Equal(0, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Concurrent_unknown_response_replays_reconcile_to_one_atomic_outcome()
    {
        var w = await SeedAsync(); var basket = Basket(w);
        string? fingerprint;
        await using (var quoteDb = database.CreateContext(w.TenantId)) fingerprint = (await Workflow(quoteDb, w).QuoteAsync(basket)).Value!.QuoteFingerprint;
        async Task<Result<CollectionSessionResult>> Attempt()
        {
            await using var db = database.CreateContext(w.TenantId);
            return await Workflow(db, w).RecordAsync(new(basket, fingerprint));
        }
        var attempts = await Task.WhenAll(Attempt(), Attempt());
        Assert.Contains(attempts, x => x.IsSuccess && x.Value!.Status == CollectionSessionStatus.Recorded);
        await using var verify = database.CreateContext(w.TenantId);
        var replay = (await Workflow(verify, w).RecordAsync(new(basket, fingerprint))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(3, replay.Collections.Count);
        Assert.Equal(3, await verify.Collections.CountAsync()); Assert.Single(await verify.MobileCollectionSessions.ToListAsync());
        foreach (var mutate in new[]
        {
            basket with { Items = [basket.Items[0] with { ConfirmedAmount = 31m }] },
            basket with { Items = [basket.Items[0] with { Service = new(CollectorOperationCodes.LandingBerthing) }] },
            basket with { Items = [basket.Items[0] with { ClientItemId = Guid.NewGuid() }] }
        }) Assert.Contains((await Workflow(verify, w).RecordAsync(new(mutate, fingerprint))).Value!.Problems, p => p.Code == "SessionIntentConflict");
        Assert.Equal(3, await verify.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Discovery_reuses_assigned_capabilities_and_returns_payer_scoped_sources_without_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var caller = new Caller(w.CollectorId, w.TenantId); var tenant = new Tenant(w.TenantId);
        var flow = Workflow(db, w, new CollectionSessionSources(db, caller, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays()));
        var discovery = (await flow.DiscoverAsync(w.PayorId)).Value!;
        Assert.Contains(discovery.Operations, x => x.OperationCode == CollectorOperationCodes.MarketFees && x.CanAdd);
        Assert.Equal(2, discovery.ServiceTerms!.Count);
        Assert.Empty(discovery.ObligationSources!);
        Assert.Equal(w.PayorId, Assert.Single(discovery.VendorFeeSources!).PayorId);
        Assert.Contains(discovery.Operations, x => x.OperationCode == "NPM_WHOLE_PAYMENT" && !x.Supported);
        Assert.Equal(0, await db.ObligationPeriods.CountAsync()); Assert.Equal(0, await db.Collections.CountAsync());
    }
    private sealed class DiscoverySender(AppDbContext db, World w) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            object response = request switch
            {
                GetCollectorMobileMenuQuery => Result<MobileMenuDto>.Success(new(w.CollectorId, "Collector", "C-1", Today,
                    [new(FacilityCode.NPM, "Market", "Market", true, true)])),
                GetCollectorOperationCapabilitiesQuery => await new GetCollectorOperationCapabilitiesQueryHandler(db,
                    new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).Handle(new(), ct),
                _ => throw new NotSupportedException()
            };
            return (TResponse)response;
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [SkippableFact]
    public async Task Electricity_direct_receipts_repeat_and_never_become_a_monthly_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w);
        foreach (var amount in new[] { 44m, 20m })
        {
            var source = Assert.Single((await new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock())
                .GetMobileEcfSourcesAsync(w.PayorId)).Value!);
            Assert.Equal("DirectCollection", source.ChargeBasis); Assert.True(source.CanPostCanonical);
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId,
                [new(Guid.NewGuid(), CollectionSessionItemKind.Electricity, amount,
                    Electricity: new(source.UtilityBillId, source.ElectricitySourceVersion, source.StallId, source.BillingYear, source.BillingMonth))]));
        }
        Assert.Equal(2, await db.Collections.CountAsync()); Assert.Equal(64m, await db.Collections.SumAsync(x => x.TotalAmount));
        var bill = await db.UtilityBills.SingleAsync(); Assert.Equal(0m, bill.ElecCharge); Assert.True(bill.ElectricityDirectCollection);
        Assert.Throws<InvalidOperationException>(() => bill.UpdateReadings(0, 1, 44, 0, 0, 0, null, "head"));
    }

    [SkippableFact]
    public async Task Prepared_utilities_accept_partial_collections_and_refuse_overpayment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var bill = UtilityBill.Create(w.StallId, Today.Year, Today.Month, 0, 1, 500m, 0, 1, 100m, "head");
        db.AddRange(bill, CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        foreach (var amount in new[] { 200m, 100m })
        {
            var source = (await new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileEcfSourcesAsync(w.PayorId)).Value!.Single();
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId, [new(Guid.NewGuid(), CollectionSessionItemKind.Electricity, amount, Electricity: new(bill.Id, source.ElectricitySourceVersion))]));
        }
        foreach (var amount in new[] { 20m, 30m })
        {
            var source = (await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileSourcesAsync(Today.Year, Today.Month)).Value!.Single();
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId, [new(Guid.NewGuid(), CollectionSessionItemKind.Water, amount, Water: new(w.StallId, Today.Year, Today.Month, bill.Id, source.WaterSourceVersion))]));
        }
        var ecf = (await new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileEcfSourcesAsync(w.PayorId)).Value!.Single();
        var water = (await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileSourcesAsync(Today.Year, Today.Month)).Value!.Single();
        Assert.Equal(200m, ecf.OutstandingAmount); Assert.Equal(50m, water.OutstandingAmount);
        var bad = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Electricity, 201m, Electricity: new(bill.Id, ecf.ElectricitySourceVersion)),
             new(Guid.NewGuid(), CollectionSessionItemKind.Water, 51m, Water: new(w.StallId, Today.Year, Today.Month, bill.Id, water.WaterSourceVersion))]);
        Assert.False((await flow.QuoteAsync(bad)).Value!.CanRecord);
        Assert.Equal(4, await db.Collections.CountAsync());
        Assert.False(bill.ElectricityDirectCollection); Assert.False(bill.WaterDirectCollection);
    }

    [SkippableFact]
    public async Task Slaughter_uses_existing_approved_calculation_and_keeps_each_transaction_boundary()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var facility = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH", municipalityId: w.TenantId);
        var service = GovernedService.Create(w.TenantId, CollectorOperationCodes.Slaughterhouse, "head");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Slaughterhouse, w.TenantId);
        db.AddRange(facility, service, classification, CollectorFacilityAssignment.Create(w.CollectorId, facility.Id, FacilityCode.SLH),
            GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.FixedAmount, 1m, null, true, true, "head"),
            RevenueClassificationPolicy.Create(classification.Id, Today, "Slaughterhouse", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 120m, Today, w.TenantId));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Slaughter, 0m, Slaughter: new(AnimalType.Hog, 2, OwnerName: "Walk-in owner")),
             new(Guid.NewGuid(), CollectionSessionItemKind.Slaughter, 0m, Slaughter: new(AnimalType.Hog, 1, OwnerName: "Walk-in owner"))]);
        var quote = (await flow.QuoteAsync(basket)).Value!;
        Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(x => x.Message)));
        Assert.Equal(360m, quote.GrandTotal); Assert.Equal(0, await db.Collections.CountAsync());
        var recorded = await Record(flow, basket);
        Assert.Equal(2, recorded.Collections.Count); Assert.Equal(2, recorded.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(await db.CollectionLines.ToListAsync(), x => Assert.Equal(classification.Id, x.RevenueClassificationId));
        Assert.Equal(0, await db.SlaughterTransactions.CountAsync());
        var invalid = basket with { ClientCollectionSessionId = Guid.NewGuid(), Items = [basket.Items[0] with { Slaughter = new(AnimalType.Hog, 0, OwnerName: "Walk-in owner") }] };
        Assert.False((await flow.QuoteAsync(invalid)).Value!.CanRecord);
        Assert.True((await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!.ExistingOutcome);
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Weighing_freezes_server_rates_and_stale_quotes_require_review()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, w.TenantId);
        var fish = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 2m, Today, w.TenantId);
        db.AddRange(classification, fish, FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo, 3m, Today, w.TenantId),
            RevenueClassificationPolicy.Create(classification.Id, Today, "Weight & Measure", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Weighing, 0m, Weighing: new(w.StallId, WeighingType.Fish, 18.5m))]);
        var quote = (await flow.QuoteAsync(basket)).Value!; Assert.Equal(37m, quote.GrandTotal);
        fish.UpdateAmount(4m, "head"); await db.SaveChangesAsync();
        Assert.Contains((await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!.Problems, p => p.Code == "QuoteStale");
        Assert.Equal(0, await db.Collections.CountAsync());
        var posted = await Record(flow, basket); Assert.Equal(74m, posted.GrandTotal);
        var snapshot = (await db.CollectionLines.SingleAsync()).CalculationSnapshot!;
        Assert.Contains("18.5", snapshot); Assert.Contains(fish.Id.ToString(), snapshot);
        fish.UpdateAmount(6m, "head"); await db.SaveChangesAsync();
        Assert.Equal(snapshot, (await db.CollectionLines.SingleAsync()).CalculationSnapshot);
        var meat = basket with { ClientCollectionSessionId = Guid.NewGuid(), Items = [basket.Items[0] with { Weighing = new(w.StallId, WeighingType.Meat, 10m) }] };
        Assert.Equal(30m, (await Record(flow, meat)).GrandTotal);
    }

    [SkippableFact]
    public async Task Direct_water_receipts_repeat_without_creating_an_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        foreach (var amount in new[] { 20m, 30m, 10m })
        {
            var source = Assert.Single((await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock())
                .GetMobileSourcesAsync(Today.Year, Today.Month)).Value!);
            Assert.Null(source.PreparedAmount); Assert.True(source.CanEnterDirect);
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId,
                [new(Guid.NewGuid(), CollectionSessionItemKind.Water, amount,
                    Water: new(w.StallId, Today.Year, Today.Month, source.UtilityBillId, source.WaterSourceVersion))]));
        }
        Assert.Equal(3, await db.Collections.CountAsync());
        Assert.Equal(60m, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(0m, (await db.UtilityBills.SingleAsync()).WaterCharge);
    }

    [SkippableFact]
    public async Task Direct_water_and_voided_replay_do_not_create_replacement_money()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Water, 83m, Water: new(w.StallId, Today.Year, Today.Month))]);
        var flow = Workflow(db, w); var recorded = await Record(flow, basket);
        var child = Assert.Single(recorded.Collections); Assert.Equal(83m, child.Amount);
        var line = await db.CollectionLines.SingleAsync(x => x.CollectionId == child.CollectionId);
        var allocations = await db.CollectionAllocations.Where(x => x.CollectionLineId == line.Id).ToListAsync();
        db.Add(CollectionCorrection.Record(w.TenantId, child.CollectionId, null, null, null, CollectionCorrectionType.Void,
            Today, DateTime.UtcNow, -83m, "Test correction", Guid.NewGuid().ToString(), "Head",
            [new(line.Id, -83m, allocations.Select(x => new CollectionCorrectionAllocationDraft(x.Id, -x.Amount)).ToArray())]));
        await db.SaveChangesAsync();
        var replay = (await flow.RecordAsync(new(basket, "original response was lost"))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal("Voided", Assert.Single(replay.Collections).Disposition);
        Assert.Equal(child.ReferenceCode, replay.Collections[0].ReferenceCode);
        Assert.Equal(1, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task One_malformed_electricity_bill_does_not_hide_an_unrelated_valid_bill()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var valid = UtilityBill.Create(w.StallId, Today.Year, Today.Month, 0, 8, 10, 0, 0, 0, "test");
        var period = Today.AddMonths(-1);
        var broken = UtilityBill.Create(w.StallId, period.Year, period.Month, 0, 8, 10, 0, 0, 0, "test");
        db.AddRange(valid, broken);
        // Historical malformed canonical evidence, deliberately without a cutover reference.
        db.Entry(broken).Property(x => x.ElectricitySettlementAuthorityState).CurrentValue = SettlementAuthority.Canonical;
        await db.SaveChangesAsync();
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var discovery = await sources.DiscoverAsync(w.PayorId, Today, default);
        Assert.Equal(valid.Id, Assert.Single(discovery.ElectricitySources!).UtilityBillId);
        Assert.Equal(0, await db.Collections.CountAsync());
    }
}
