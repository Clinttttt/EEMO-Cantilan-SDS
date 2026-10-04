using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
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
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The NPM daily stall fee on PostgreSQL (Phase 2.4). Before the Head turns the switch on, a day is a legacy row exactly as
/// before. From that business date a new payment is ONE canonical Collection with an SRC under the existing stall-rent
/// classification; the day row is the operational day and a projection of that Collection. A day is never both, a posted
/// Collection is corrected only by a void, and month settlement follows the day's own authority.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NpmDailyCanonicalTests(PostgresFixture db)
{
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly Effective = new(2000, 1, 1);

    private sealed record World(Guid TenantId, Guid AdminId, Guid CollectorId, Guid StallA, Guid StallB, Guid FishStall);

    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "mobile-collector" : "office-admin";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "npm-daily";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class FixedCode : ITenantContext { public string TenantCode => "npm-daily"; }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private sealed class NoCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static NpmDailyCanonicalPoster Poster(AppDbContext ctx, Guid tenantId, ICurrentUserService actor) =>
        new(ctx, actor, new FixedTenant(tenantId), new GovernedCanonicalAuthority(ctx, new FixedTenant(tenantId)));

    private static RecordDailyCollectionCommandHandler Record(AppDbContext ctx, Guid tenantId, ICurrentUserService actor) => new(
        new DailyCollectionRepository(ctx), new PaymentRepository(ctx), new DbOrNumberRegistry(ctx), new StallRepository(ctx),
        new CollectorRepository(ctx), actor, new UnitOfWork(ctx), new NoCache(), new FeeRateResolver(ctx), new FixedCode(),
        Poster(ctx, tenantId, actor));

    private static NpmMonthSettlementService Settlement(AppDbContext ctx) => new(
        new DailyCollectionRepository(ctx), new NpmMarketClosureRepository(ctx), new FeeRateResolver(ctx), new Clock());

    private static SettleNpmDaysCommandHandler SettleDays(AppDbContext ctx, Guid tenantId, ICurrentUserService actor) => new(
        new DailyCollectionRepository(ctx), new PaymentRepository(ctx), new StallRepository(ctx), new CollectorRepository(ctx), actor,
        new NpmMarketClosureRepository(ctx), new UnitOfWork(ctx), new NoCache(), new FeeRateResolver(ctx), Settlement(ctx), new FixedCode(),
        new Clock(), Poster(ctx, tenantId, actor));

    private Actor Collector(World w) => new(w.CollectorId, w.TenantId, "Collector");
    private Actor Admin(World w) => new(w.AdminId, w.TenantId, "Admin");

    /// <summary>An NPM with three let stalls at ₱30 a day (one a Fish stall with a ₱1/kilo weighing rate) and an assigned collector.</summary>
    private async Task<World> SeedAsync(bool canonical, bool assign = true)
    {
        var tenant = Municipality.Create($"nd-{Guid.NewGuid():N}"[..12], "NPM Daily", "Province", MunicipalityStatus.Active,
            tenantCode: $"npmdaily-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", $"nd-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        await using var ctx = db.CreateContext(tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Public Market", "NPM", municipalityId: tenant.Id);
        ctx.Add(npm);
        var lettingFrom = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);
        Stall Let(string no, MarketSection section, string occupant)
        {
            var stall = Stall.Create(npm.Id, no, 900m, ApplicableFees.BaseRental, section, municipalityId: tenant.Id);
            ctx.Add(stall);
            ctx.Add(Contract.Create(stall.Id, occupant, occupant, lettingFrom, 5, 900m, createdBy: "test"));
            return stall;
        }
        var a = Let("V-01", MarketSection.VegetableArea, "Ana Vendor");
        var b = Let("V-02", MarketSection.VegetableArea, "Ben Vendor");
        var fish = Let("F-01", MarketSection.FishSection, "Fe Fishmonger");
        ctx.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Effective, tenant.Id));
        ctx.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 1m, Effective, tenant.Id));
        var rent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, tenant.Id);
        ctx.Add(rent);
        ctx.Add(RevenueClassificationPolicy.Create(rent.Id, Effective, "Stall Rental", RevenueInstrumentType.OfficialReceipt, tenant.Id));
        if (assign) ctx.Add(CollectorFacilityAssignment.Create(collector.Id, npm.Id, FacilityCode.NPM));
        await ctx.SaveChangesAsync();
        var world = new World(tenant.Id, Guid.NewGuid(), collector.Id, a.Id, b.Id, fish.Id);
        if (canonical) await TurnOnAsync(world);
        return world;
    }

    private async Task TurnOnAsync(World w)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var service = GovernedService.Create(w.TenantId, CollectorOperationCodes.NpmDaily, "head");
        ctx.Add(service);
        ctx.Add(GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "head"));
        await ctx.SaveChangesAsync();
    }

    private async Task<decimal> IncomeAsync(World w, string rowKey)
    {
        await using var c = db.CreateContext(w.TenantId);
        var handler = new EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome.GetOfficialMonthlyIncomeQueryHandler(
            c, new LegacyMonthlyIncomeReader(c), Admin(w), new FixedTenant(w.TenantId), new Clock());
        var statement = (await handler.Handle(new(Today.Year, Today.Month), CancellationToken.None)).Value!;
        return statement.Groups.SelectMany(g => g.Rows).Single(r => r.Key == rowKey).Months[Today.Month - 1].Total;
    }

    private async Task<decimal> PayableAsync(World w, Guid stallId)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var stall = (await new StallRepository(ctx).GetByIdAsync(stallId, CancellationToken.None))!;
        return (await Settlement(ctx).ComputePayableAsync(stall, Today.Year, Today.Month, CancellationToken.None)).Amount;
    }

    // ── Before the switch: legacy, exactly as before ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task BeforeTheSwitch_ADayIsALegacyRow_WithItsTypedReceipt_AndNoCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var owedBefore = await PayableAsync(w, w.StallA);

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var result = await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today, true, null, "OR-LEGACY-1", Guid.NewGuid()), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        await using var verify = db.CreateContext(w.TenantId);
        var day = await verify.DailyCollections.SingleAsync();
        Assert.Equal((true, SettlementAuthority.Legacy, "OR-LEGACY-1", (Guid?)null), (day.IsPaid, day.SettlementAuthorityState, day.ORNumber, day.CanonicalCollectionId));
        Assert.Empty(await verify.Collections.ToListAsync());
        Assert.Equal(30m, await IncomeAsync(w, "RENT_NPM"));                       // reported by the legacy reader, once
        Assert.Equal(owedBefore - 30m, await PayableAsync(w, w.StallA));           // settlement reads the legacy day
    }

    // ── From the switch: canonical ───────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task FromTheSwitch_ADayIsOneCollectionWithAnSrc_UnderStallRent_ReplaysTheSame_AndIsNeverCollectedTwice()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var owedBefore = await PayableAsync(w, w.StallA);
        var op = Guid.NewGuid();
        // A typed receipt number sent by an old client is ignored on the canonical path.
        var command = new RecordDailyCollectionCommand(w.StallA, Today, true, null, "TYPED-123", op);

        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(command, CancellationToken.None)).IsSuccess);

        string src;
        Guid collectionId;
        await using (var verify = db.CreateContext(w.TenantId))
        {
            var collection = await verify.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
            (src, collectionId) = (collection.ReferenceCode, collection.Id);
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", src);
            Assert.Equal((30m, w.CollectorId, Today), (collection.TotalAmount, collection.CollectorId!.Value, collection.BusinessDate));
            var line = Assert.Single(collection.Lines);
            var rent = await verify.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.PermanentStallRent);
            Assert.Equal(rent.Id, line.RevenueClassificationId);                    // the existing classification, by its id
            var allocation = Assert.Single(line.Allocations);
            var day = await verify.DailyCollections.SingleAsync();
            Assert.Equal((CollectionSourceKind.DailyCollection, day.Id, 30m), (allocation.SourceKind, allocation.SourceId, allocation.Amount));
            Assert.Equal((w.StallA, Today, true, SettlementAuthority.Canonical, (Guid?)collection.Id),
                (day.StallId, day.CollectionDate, day.IsPaid, day.SettlementAuthorityState, day.CanonicalCollectionId));
            Assert.True(string.IsNullOrEmpty(day.ORNumber));                        // no typed serial on the canonical path
            Assert.Empty(await verify.AccountableDocuments.ToListAsync());
        }
        Assert.Equal(owedBefore - 30m, await PayableAsync(w, w.StallA));           // settlement sees the day paid

        // Retry of the same operation: the same Collection, nothing new.
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(command, CancellationToken.None)).IsSuccess);
            var posted = await Poster(ctx, w.TenantId, Collector(w)).FindPostedAsync(op);
            Assert.Equal((collectionId, src), (posted!.CollectionId, posted.ReferenceCode));
        }
        // The same id reused for a different day is a conflict, never a second payment.
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var conflict = await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today.AddDays(-1), true, null, null, op), CancellationToken.None);
            Assert.Equal(ResultStatus.Conflict, conflict.Status);
        }
        // The same stall and day again under a NEW operation: the day is already paid, so no money is taken again.
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today, true, null, null, Guid.NewGuid()), CancellationToken.None)).IsSuccess);

        await using var final = db.CreateContext(w.TenantId);
        Assert.Equal(1, await final.Collections.CountAsync());
        Assert.Equal(1, await final.DailyCollections.CountAsync());
        Assert.Equal(30m, await IncomeAsync(w, "RENT_NPM"));
    }

    [SkippableFact]
    public async Task AnUnassignedCollector_AndAnotherTenantsStall_AreRefused_AndWriteNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var unassigned = await SeedAsync(canonical: true, assign: false);
        await using (var ctx = db.CreateContext(unassigned.TenantId))
        {
            var refused = await Record(ctx, unassigned.TenantId, Collector(unassigned)).Handle(
                new RecordDailyCollectionCommand(unassigned.StallA, Today, true, null, null, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal(ResultStatus.Forbidden, refused.Status);
            Assert.Empty(await ctx.Collections.ToListAsync());
            Assert.Empty(await ctx.DailyCollections.ToListAsync());
        }

        await db.ResetAsync();
        var a = await SeedAsync(canonical: true);
        var b = await SeedAsync(canonical: true);
        await using (var ctx = db.CreateContext(b.TenantId))   // tenant B's collector naming tenant A's stall
        {
            var refused = await Record(ctx, b.TenantId, Collector(b)).Handle(
                new RecordDailyCollectionCommand(a.StallA, Today, true, null, null, Guid.NewGuid()), CancellationToken.None);
            Assert.False(refused.IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        await using var other = db.CreateContext(a.TenantId);
        Assert.Empty(await other.Collections.ToListAsync());
        Assert.Empty(await other.DailyCollections.ToListAsync());
    }

    // ── History is untouched and nothing is counted twice ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task AnEarlierLegacyDayAndALaterCanonicalDay_AreEachCountedOnce_Everywhere_AndRemittanceNeverMovesIncome()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        await using (var ctx = db.CreateContext(w.TenantId))    // stall A, collected before the switch
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today, true, null, "OR-LEGACY-1", Guid.NewGuid()), CancellationToken.None)).IsSuccess);
        await TurnOnAsync(w);
        await using (var ctx = db.CreateContext(w.TenantId))    // stall B, collected after it
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallB, Today, true, null, null, Guid.NewGuid()), CancellationToken.None)).IsSuccess);

        await using var verify = db.CreateContext(w.TenantId);
        var legacyDay = await verify.DailyCollections.SingleAsync(x => x.StallId == w.StallA);
        Assert.Equal((SettlementAuthority.Legacy, "OR-LEGACY-1", true), (legacyDay.SettlementAuthorityState, legacyDay.ORNumber, legacyDay.IsPaid));
        var collection = await verify.Collections.SingleAsync();                       // only the later day is a Collection
        Assert.Equal(60m, await IncomeAsync(w, "RENT_NPM"));                           // 30 legacy + 30 canonical, never 90

        var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(verify).GetAsync(w.TenantId, Today, Today);
        Assert.Equal(30m, Assert.Single(activity, e => e.Authority == "Canonical").Amount);
        Assert.Equal(collection.ReferenceCode, activity.Single(e => e.Authority == "Canonical").ReferenceCode);
        Assert.Equal(30m, Assert.Single(activity, e => e.Authority != "Canonical").Amount);

        var report = await new CollectorReportQueries(verify).GetCollectionsAsync(w.CollectorId, Today.AddDays(-1), Today);
        var canonicalLine = Assert.Single(report.Lines, l => l.IsCanonical);
        Assert.Equal((30m, collection.ReferenceCode, "Daily Fee"), (canonicalLine.Amount, canonicalLine.DocumentNumber, canonicalLine.Nature));
        Assert.Equal(30m, Assert.Single(report.Lines, l => !l.IsCanonical).Amount);

        var register = (await new CollectionsReportWorkflow(verify, Admin(w), new FixedTenant(w.TenantId))
            .GetRegisterAsync(Today, Today, null, null, null)).Value!;
        Assert.Equal((collection.ReferenceCode, 30m), (Assert.Single(register.Rows).ReferenceCode, register.Rows[0].Amount));
        var mine = (await new CollectionsReportWorkflow(verify, Collector(w), new FixedTenant(w.TenantId)).GetMyRegisterAsync(Today, Today)).Value!;
        Assert.Equal(collection.ReferenceCode, Assert.Single(mine.Rows).ReferenceCode);

        var remit = new RemittanceWorkflow(verify, Admin(w), new FixedTenant(w.TenantId));
        var before = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors.Single(x => x.CollectorId == w.CollectorId);
        Assert.Equal((30m, 0m, 30m), (before.Collected, before.Remitted, before.Unremitted));   // the canonical day only
        var scope = (await remit.GetScopeAsync(w.CollectorId, Today.AddDays(-1), Today, null)).Value!;
        Assert.Equal(collection.Id, Assert.Single(scope.Collections).CollectionId);
        var recorded = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.CollectorId, Today, Today.AddDays(-1), Today, null, null, 30m, "proof", null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var after = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors.Single(x => x.CollectorId == w.CollectorId);
        Assert.Equal((30m, 30m, 0m), (after.Collected, after.Remitted, after.Unremitted));
        Assert.Equal(1, await verify.Collections.CountAsync());                        // a remittance creates no Collection
        Assert.Equal(60m, await IncomeAsync(w, "RENT_NPM"));                           // and never moves income
    }

    // ── Correction is a void, and settlement follows it ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task ACanonicalDayCannotBeUnmarked_ItIsVoided_WhichRemovesItsIncome_AndTheDayIsOwedAgain()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var owedBefore = await PayableAsync(w, w.StallA);
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today, true, null, null, Guid.NewGuid()), CancellationToken.None)).IsSuccess);

        await using (var ctx = db.CreateContext(w.TenantId))   // the old "mark unpaid" is not a correction of posted money
        {
            var unmark = await Record(ctx, w.TenantId, Admin(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today, false), CancellationToken.None);
            Assert.Equal(ResultStatus.Conflict, unmark.Status);
            Assert.True((await ctx.DailyCollections.AsNoTracking().SingleAsync()).IsPaid);
        }

        Guid collectionId;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            collectionId = (await ctx.Collections.SingleAsync()).Id;
            Assert.Equal(ResultStatus.Forbidden, (await Poster(ctx, w.TenantId, Collector(w)).VoidAsync(collectionId, "wrong stall")).Status);
            var voided = await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(collectionId, "Collected against the wrong stall");
            Assert.True(voided.IsSuccess, voided.Error);
            Assert.False((await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(collectionId, "again")).IsSuccess);   // corrected once
        }

        await using (var verify = db.CreateContext(w.TenantId))
        {
            var collection = await verify.Collections.SingleAsync();                    // append-only: still there, amount untouched
            Assert.Equal(30m, collection.TotalAmount);
            var correction = await verify.CollectionCorrections.SingleAsync();
            Assert.Equal((CollectionCorrectionType.Void, -30m, collectionId), (correction.CorrectionType, correction.FinancialEffectAmount, correction.OriginalCollectionId));
            var day = await verify.DailyCollections.SingleAsync();
            Assert.Equal((false, SettlementAuthority.Canonical, (Guid?)null), (day.IsPaid, day.SettlementAuthorityState, day.CanonicalCollectionId));
            var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(verify).GetAsync(w.TenantId, Today, Today);
            var entry = Assert.Single(activity);
            Assert.Equal(("Voided", 0m), (entry.Disposition, entry.NetAmount));
            var report = await new CollectorReportQueries(verify).GetCollectionsAsync(w.CollectorId, Today.AddDays(-1), Today);
            Assert.DoesNotContain(report.Lines, l => l.Nature == "Daily Fee");
        }
        Assert.Equal(0m, await IncomeAsync(w, "RENT_NPM"));                            // through the correction, not an exclusion
        Assert.Equal(owedBefore, await PayableAsync(w, w.StallA));                     // the day is owed again

        await using (var ctx = db.CreateContext(w.TenantId))   // and can be collected again: a new Collection, a new SRC
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, Today, true, null, null, Guid.NewGuid()), CancellationToken.None)).IsSuccess);
        await using var final = db.CreateContext(w.TenantId);
        Assert.Equal(2, await final.Collections.CountAsync());
        Assert.Equal(30m, await IncomeAsync(w, "RENT_NPM"));
        Assert.Equal(owedBefore - 30m, await PayableAsync(w, w.StallA));
    }

    // ── Several days and weighing ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task SeveralDaysSettledTogether_AreOneCollection_WithOneAllocationPerDay()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var days = new[] { Today.AddDays(-2), Today.AddDays(-1), Today };

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var settled = await SettleDays(ctx, w.TenantId, Collector(w)).Handle(new SettleNpmDaysCommand(w.StallA, days, "TYPED-9"), CancellationToken.None);
            Assert.True(settled.IsSuccess, settled.Error);
        }

        await using var verify = db.CreateContext(w.TenantId);
        var collection = await verify.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(90m, collection.TotalAmount);
        var allocations = Assert.Single(collection.Lines).Allocations;
        Assert.Equal(3, allocations.Count);
        var rows = await verify.DailyCollections.OrderBy(x => x.CollectionDate).ToListAsync();
        Assert.Equal(days, rows.Select(x => x.CollectionDate));
        Assert.All(rows, r => Assert.Equal((true, SettlementAuthority.Canonical, (Guid?)collection.Id), (r.IsPaid, r.SettlementAuthorityState, r.CanonicalCollectionId)));
        Assert.All(rows, r => Assert.True(string.IsNullOrEmpty(r.ORNumber)));
        Assert.Equal(rows.Select(r => r.Id).Order(), allocations.Select(a => a.SourceId).Order());
    }

    [SkippableFact]
    public async Task WeighingRecordedWithACanonicalDay_StaysWeightAndMeasure_AndIsNeverFoldedIntoTheStallFee()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.FishStall, Today, true, 5m, null, Guid.NewGuid()), CancellationToken.None)).IsSuccess);

        await using var verify = db.CreateContext(w.TenantId);
        Assert.Equal(30m, (await verify.Collections.SingleAsync()).TotalAmount);          // the stall fee only
        var day = await verify.DailyCollections.SingleAsync();
        Assert.Equal((5m, 5m), (day.FishKilos!.Value, day.FishFeeAmountFrozen!.Value));   // weighing evidence stays on the day row
        Assert.Equal(30m, await IncomeAsync(w, "RENT_NPM"));
        Assert.Equal(5m, await IncomeAsync(w, "WEIGHT_AND_MEASURE"));
        var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(verify).GetAsync(w.TenantId, Today, Today);
        Assert.Equal(30m, Assert.Single(activity, e => e.Authority == "Canonical").Amount);
        Assert.Equal(5m, Assert.Single(activity, e => e.Authority != "Canonical").Amount);   // the weighing, on its existing path
    }
}
