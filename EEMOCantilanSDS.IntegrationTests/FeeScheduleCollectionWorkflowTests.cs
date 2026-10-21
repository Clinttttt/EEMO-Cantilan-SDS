using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Collector Mobile canonical Tabo and Slaughterhouse collections on PostgreSQL (Phase 2.2). The amount is the office's EXISTING
/// fee schedule (never typed); no physical serial; one SRC; idempotent; refused outside the Head's enablement; every report
/// counts the collection exactly once and a remittance never moves income.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeeScheduleCollectionWorkflowTests(PostgresFixture db)
{
    private sealed class FailSecondCollectionSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int _collections;
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<EEMOCantilanSDS.Domain.Entities.Revenue.Collection>()
                .Any(x => x.State == EntityState.Added) && ++_collections == 2)
                throw new IOException("Injected failure after the first canonical child save");
            return ValueTask.FromResult(result);
        }
    }

    [SkippableFact]
    public async Task Tabo_batch_late_failure_rolls_back_child_money_and_retry_uses_original_ids()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        TaboBatchRequest request;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var a = TpmVendor.Create("Vendor A", "Fish");
            var b = TpmVendor.Create("Vendor B", "Fruit");
            ctx.AddRange(a, b);
            await ctx.SaveChangesAsync();
            var items = new[] { Tabo() with { VendorId = a.Id }, Tabo() with { VendorId = b.Id } };
            var quote = (await Workflow(ctx, w.TenantId, w.CollectorId).QuoteTaboBatchAsync(new(items))).Value!;
            Assert.True(quote.CanRecord);
            request = new(items, quote.Fingerprint);
        }
        await using (var raw = db.CreateRawConnection())
        await using (var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(raw.ConnectionString)
            .AddInterceptors(new EEMOCantilanSDS.Infrastructure.Persistence.Interceptors.MunicipalityStampInterceptor(), new FailSecondCollectionSave())
            .Options, new FixedTenant(w.TenantId)))
            await Assert.ThrowsAsync<IOException>(() => Workflow(failing, w.TenantId, w.CollectorId).RecordTaboBatchAsync(request));
        await using var verify = db.CreateContext(w.TenantId);
        Assert.Empty(await verify.Collections.ToListAsync());
        Assert.Empty(await verify.CollectionLines.ToListAsync());
        Assert.Empty(await verify.PostingOperations.ToListAsync());
        var retry = await Workflow(verify, w.TenantId, w.CollectorId).RecordTaboBatchAsync(request);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(2, retry.Value!.Collections.Count);
        Assert.Equal(2, await verify.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Tabo_batch_preserves_vendor_day_evidence_replays_and_preflights_every_child()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using var ctx = db.CreateContext(w.TenantId);
        var a = TpmVendor.Create("Vendor A", "Fish");
        var b = TpmVendor.Create("Vendor B", "Fruit");
        ctx.AddRange(a, b);
        await ctx.SaveChangesAsync();
        var flow = Workflow(ctx, w.TenantId, w.CollectorId);
        var items = new[] { Tabo() with { VendorId = a.Id }, Tabo() with { VendorId = b.Id } };
        var bad = await flow.QuoteTaboBatchAsync(new([items[0], items[1] with { ReceivedAmount = 99m }]));
        Assert.False(bad.Value!.CanRecord);
        Assert.Empty(await ctx.Collections.ToListAsync());
        var quote = (await flow.QuoteTaboBatchAsync(new(items))).Value!;
        Assert.True(quote.CanRecord);
        Assert.Equal(200m, quote.Total);
        var request = new TaboBatchRequest(items, quote.Fingerprint);
        var posted = await flow.RecordTaboBatchAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(2, posted.Value!.Collections.Count);
        Assert.Equal(posted.Value.Collections.Select(x => x.ReferenceCode),
            (await flow.RecordTaboBatchAsync(request)).Value!.Collections.Select(x => x.ReferenceCode));
        Assert.Equal(2, await ctx.Collections.CountAsync());
        Assert.Empty(await ctx.TpmAttendances.ToListAsync()); // no duplicate legacy money projection
        var lines = await ctx.CollectionLines.ToListAsync();
        Assert.Contains(lines, x => x.CalculationSnapshot!.Contains(a.Id.ToString()));
        Assert.Contains(lines, x => x.CalculationSnapshot!.Contains(b.Id.ToString()));
        Assert.Equal(ResultStatus.Conflict, (await flow.RecordTaboBatchAsync(request with {
            Items = [items[0] with { ReceivedAmount = 50m }, items[1]] })).Status);
        Assert.Equal(2, await ctx.Collections.CountAsync());
    }

    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly Effective = new(2000, 1, 1);

    private sealed record World(Guid TenantId, Guid AdminId, Guid CollectorId);

    private sealed class Actor(Guid userId, Guid tenantId, string role, bool collector) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => collector ? "mobile-collector" : "office-admin";
        public string? Role => role;
        public Guid? CollectorId => collector ? userId : null;
        public string? MunicipalityCode => "fee-schedule";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class MarketDay(DayOfWeek day) : ITpmMarketDayProvider
    {
        public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default) => Task.FromResult(day);
        public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DateOnly>>([]);
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private FeeScheduleCollectionWorkflow Workflow(AppDbContext ctx, Guid tenantId, Guid collectorId,
        DayOfWeek? marketDay = null, string role = "Collector") =>
        new(ctx, new Actor(collectorId, tenantId, role, collector: true), new FixedTenant(tenantId),
            new FeeRateResolver(ctx), new MarketDay(marketDay ?? Today.DayOfWeek), new Clock());

    /// <summary>A tenant with TPM/SLH, rates, TABO/SLAUGHTERHOUSE classification policies, an assigned collector, and (optionally) the services enabled.</summary>
    private async Task<World> SeedAsync(bool enabled, bool taboRate = true, bool hogRate = true, bool referenceTenant = false)
    {
        var tenant = Municipality.Create($"fs-{Guid.NewGuid():N}"[..12], "Fee Schedule", "Province", MunicipalityStatus.Active,
            tenantCode: $"feesched-{Guid.NewGuid():N}"[..28].ToLowerInvariant(), isDefault: referenceTenant);
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", $"fs-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        await using var ctx = db.CreateContext(tenant.Id);
        var tpm = Facility.Create(FacilityCode.TPM, "Taboan Market", "TPM", municipalityId: tenant.Id);
        var slh = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH", municipalityId: tenant.Id);
        ctx.AddRange(tpm, slh);
        foreach (var (code, name, instrument) in new[]
                 {
                     (RevenueClassificationCodes.Tabo, "Tabo", RevenueInstrumentType.OfficialReceipt),
                     (RevenueClassificationCodes.Slaughterhouse, "Slaughterhouse", RevenueInstrumentType.OfficialReceipt)
                 })
        {
            var classification = RevenueClassification.Create(code, tenant.Id);
            ctx.Add(classification);
            ctx.Add(RevenueClassificationPolicy.Create(classification.Id, Effective, name, instrument, tenant.Id));
        }
        if (taboRate) ctx.Add(FacilityRate.Create(FacilityCode.TPM, FeeRateKey.TpmVendorDay, 100m, Effective, tenant.Id));
        if (hogRate) ctx.Add(FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 80m, Effective, tenant.Id));
        ctx.Add(FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 150m, Effective, tenant.Id));
        ctx.Add(SlaughterAnimalRate.Create("Goat", 40m, tenant.Id));
        ctx.Add(CollectorFacilityAssignment.Create(collector.Id, tpm.Id, FacilityCode.TPM));
        ctx.Add(CollectorFacilityAssignment.Create(collector.Id, slh.Id, FacilityCode.SLH));
        if (enabled)
            foreach (var code in new[] { CollectorOperationCodes.Tabo, CollectorOperationCodes.Slaughterhouse })
            {
                var service = GovernedService.Create(tenant.Id, code, "head");
                ctx.Add(service);
                ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, Effective, GovernedServiceBasis.DirectApprovedAmount,
                    null, null, true, true, "head"));
            }
        await ctx.SaveChangesAsync();
        return new World(tenant.Id, Guid.NewGuid(), collector.Id);
    }

    private static FeeSchedulePostRequest Tabo(decimal amount = 100m, string vendor = "Maria Vendor", string goods = "Vegetables", Guid? op = null) =>
        new(op ?? Guid.NewGuid(), CollectorOperationCodes.Tabo, Today, amount, VendorName: vendor, Goods: goods);

    private static FeeSchedulePostRequest Slaughter(decimal amount, AnimalType animal = AnimalType.Hog, int heads = 3,
        string? custom = null, Guid? op = null) =>
        new(op ?? Guid.NewGuid(), CollectorOperationCodes.Slaughterhouse, Today, amount, Animal: animal, CustomAnimalName: custom,
            Heads: heads, OwnerName: "Juan Owner");

    // ── Tabo ─────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Tabo_PostsTheExistingVendorDayFee_WithNoSerial_ReturnsSrc_ReplaysTheSame_AndWritesNoLegacyAttendance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        var request = Tabo();

        string src;
        Guid collectionId;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var first = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(request);
            Assert.True(first.IsSuccess, first.Error);
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", first.Value!.ReferenceCode);
            Assert.Equal(100m, first.Value.Amount);
            (src, collectionId) = (first.Value.ReferenceCode, first.Value.CollectionId);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var replay = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(request);
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((collectionId, src, true), (replay.Value!.CollectionId, replay.Value.ReferenceCode, replay.Value.ExistingOutcome));
        }

        await using var verify = db.CreateContext(w.TenantId);
        var collection = await verify.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal((100m, w.CollectorId, src), (collection.TotalAmount, collection.CollectorId!.Value, collection.ReferenceCode));
        Assert.Equal(CollectionSourceKind.GovernedService, Assert.Single(collection.Lines).SourceKind);
        Assert.Single(await verify.TpmVendors.ToListAsync());                 // the registry vendor, once
        Assert.Empty(await verify.TpmAttendances.ToListAsync());              // no legacy operational row: never both
        Assert.Empty(await verify.AccountableDocuments.ToListAsync());        // no physical form, no OR/CT consumed
        Assert.Single(await verify.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task Tabo_IsRefusedWithoutWriting_WhenNotEnabled_WrongAmount_NotMarketDay_Duplicate_Unassigned_OrNotACollector()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var off = await SeedAsync(enabled: false);
        await using (var ctx = db.CreateContext(off.TenantId))
        {
            var refused = await Workflow(ctx, off.TenantId, off.CollectorId).PostMobileAsync(Tabo());
            Assert.False(refused.IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
            Assert.Empty(await ctx.TpmVendors.ToListAsync());                 // a rejected post never registers a vendor
        }

        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            Assert.False((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(Tabo(amount: 90m))).IsSuccess);   // typed ≠ schedule
            Assert.False((await Workflow(ctx, w.TenantId, w.CollectorId, marketDay: Today.AddDays(1).DayOfWeek)
                .PostMobileAsync(Tabo())).IsSuccess);                                                                          // not a market day
            Assert.Empty(await ctx.Collections.ToListAsync());
            Assert.Empty(await ctx.TpmVendors.ToListAsync());
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(Tabo())).IsSuccess);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var duplicate = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(Tabo(vendor: "MARIA VENDOR"));   // same vendor, same day
            Assert.False(duplicate.IsSuccess);
            Assert.Single(await ctx.Collections.ToListAsync());
            Assert.Single(await ctx.TpmVendors.ToListAsync());
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var admin = await Workflow(ctx, w.TenantId, w.CollectorId, role: "Admin").PostMobileAsync(Tabo(vendor: "Another"));
            Assert.Equal(ResultStatus.Forbidden, admin.Status);
            var stranger = await Workflow(ctx, w.TenantId, Guid.NewGuid()).PostMobileAsync(Tabo(vendor: "Another"));
            Assert.Equal(ResultStatus.Forbidden, stranger.Status);
        }
    }

    [SkippableFact]
    public async Task Tabo_RefusesAnUnstatedRate_ACrossTenantCollector_AndAnIdempotencyConflict()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var noRate = await SeedAsync(enabled: true, taboRate: false);
        await using (var ctx = db.CreateContext(noRate.TenantId))
        {
            Assert.False((await Workflow(ctx, noRate.TenantId, noRate.CollectorId).PostMobileAsync(Tabo())).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }

        await db.ResetAsync();
        var a = await SeedAsync(enabled: true);
        var b = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(b.TenantId))   // tenant A's collector acting inside tenant B
            Assert.Equal(ResultStatus.Forbidden,
                (await Workflow(ctx, b.TenantId, a.CollectorId).PostMobileAsync(Tabo())).Status);

        var op = Guid.NewGuid();
        await using (var ctx = db.CreateContext(a.TenantId))
            Assert.True((await Workflow(ctx, a.TenantId, a.CollectorId).PostMobileAsync(Tabo(op: op))).IsSuccess);
        await using (var ctx = db.CreateContext(a.TenantId))
        {
            var conflict = await Workflow(ctx, a.TenantId, a.CollectorId).PostMobileAsync(Tabo(vendor: "Different Vendor", op: op));
            Assert.Equal(ResultStatus.Conflict, conflict.Status);
            Assert.Single(await ctx.Collections.ToListAsync());
        }
    }

    [SkippableFact]
    public async Task Tabo_IsCountedOnceInMonthlyIncome_CollectorReport_AndRemittance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(Tabo())).IsSuccess);

        await ProveExactlyOnceAsync(w, "TABO", 100m, "Tabo");
    }

    // ── Slaughterhouse ───────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Slaughter_PostsTheExistingTransactionAmount_ReplaysTheSame_AndWritesNoLegacyTransaction()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        // The oracle is the existing domain calculation itself, not a second algorithm.
        var oracle = SlaughterTransaction.CreateHog(Guid.NewGuid(), w.CollectorId, "Juan Owner", 3, string.Empty, Today, "t", ratePerHead: 80m).TotalAmount;
        Assert.Equal(240m, oracle);
        var request = Slaughter(oracle);

        string src;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var first = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(request);
            Assert.True(first.IsSuccess, first.Error);
            src = first.Value!.ReferenceCode;
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", src);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var replay = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(request);
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((src, true), (replay.Value!.ReferenceCode, replay.Value.ExistingOutcome));
        }

        await using var verify = db.CreateContext(w.TenantId);
        var collection = await verify.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal((oracle, w.CollectorId), (collection.TotalAmount, collection.CollectorId!.Value));
        Assert.Single(collection.Lines);                                    // one transaction → one Collection, one line
        Assert.Empty(await verify.SlaughterTransactions.ToListAsync());     // no legacy row: never both
        Assert.Empty(await verify.AccountableDocuments.ToListAsync());
    }

    [SkippableFact]
    public async Task ACanonicalSlaughterCollectionAppearsOnceInTheSlaughterhouseWorkspace_NeverAsAnEditableLegacyRow()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        var request = Slaughter(240m);
        string src;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var first = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(request);
            Assert.True(first.IsSuccess, first.Error);
            src = first.Value!.ReferenceCode;
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(request)).IsSuccess);   // a replay is the same collection
        }
        await using var verify = db.CreateContext(w.TenantId);
        var repo = new EEMOCantilanSDS.Infrastructure.Repositories.SlaughterRepository(verify);

        var overview = await repo.GetOverviewAsync(Today.Year, Today.Month);
        Assert.Equal((1, 3, 240m, 3), (overview.TotalTransactions, overview.TotalHeads, overview.TotalCollected, overview.HogCount));   // counted exactly once
        var group = Assert.Single(await repo.GetGroupedTransactionsByMonthAsync(Today.Year, Today.Month));
        Assert.Equal("Juan Owner", group.OwnerName);
        var row = Assert.Single(group.LatestTransactions);
        Assert.Equal((src, src, 3, 240m, AnimalType.Hog), (row.ReferenceCode, row.ORNumber, row.NumberOfHeads, row.TotalAmount, row.AnimalType));
        var history = await repo.GetOwnerTransactionHistoryAsync("Juan Owner", Today.Year, Today.Month);
        Assert.Equal(src, Assert.Single(Assert.Single(history.TransactionGroups).Transactions).ReferenceCode);
        var year = await repo.GetHistoryAsync(Today.Year);
        Assert.Equal(240m, year.Yearly.Single(y => y.Year == Today.Year).TotalCollected);
        Assert.Empty(await verify.SlaughterTransactions.ToListAsync());   // reading it never creates a legacy row

    }

    [SkippableFact]
    public async Task Slaughter_UsesLargeAnimalAndApprovedCustomRates_AndRefusesUnapprovedUnstatedOrTypedAmounts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = Workflow(ctx, w.TenantId, w.CollectorId);
            var cow = await workflow.PostMobileAsync(Slaughter(300m, AnimalType.Cow, 2));
            Assert.True(cow.IsSuccess, cow.Error);
            var goat = await workflow.PostMobileAsync(Slaughter(120m, AnimalType.Other, 3, "goat"));
            Assert.True(goat.IsSuccess, goat.Error);
            Assert.Equal(420m, (await ctx.Collections.ToListAsync()).Sum(x => x.TotalAmount));
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = Workflow(ctx, w.TenantId, w.CollectorId);
            Assert.False((await workflow.PostMobileAsync(Slaughter(10m, AnimalType.Other, 1, "Unicorn"))).IsSuccess);   // not an approved animal
            Assert.False((await workflow.PostMobileAsync(Slaughter(1m, AnimalType.Hog, 3))).IsSuccess);                 // typed ≠ schedule
            Assert.False((await workflow.PostMobileAsync(Slaughter(240m, AnimalType.Hog, 0))).IsSuccess);               // no heads
            Assert.Equal(2, await ctx.Collections.CountAsync());
        }

        await db.ResetAsync();
        var noRate = await SeedAsync(enabled: true, hogRate: false);
        await using var none = db.CreateContext(noRate.TenantId);
        Assert.False((await Workflow(none, noRate.TenantId, noRate.CollectorId).PostMobileAsync(Slaughter(240m))).IsSuccess);
        Assert.Empty(await none.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Slaughter_IsRefusedWhenNotEnabled_ForAnUnassignedCollector_AndAdminRole()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var off = await SeedAsync(enabled: false);
        await using (var ctx = db.CreateContext(off.TenantId))
        {
            Assert.False((await Workflow(ctx, off.TenantId, off.CollectorId).PostMobileAsync(Slaughter(240m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using var verify = db.CreateContext(w.TenantId);
        Assert.Equal(ResultStatus.Forbidden, (await Workflow(verify, w.TenantId, Guid.NewGuid()).PostMobileAsync(Slaughter(240m))).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Workflow(verify, w.TenantId, w.CollectorId, role: "Admin").PostMobileAsync(Slaughter(240m))).Status);
        Assert.Empty(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Slaughter_IsCountedOnceInMonthlyIncome_CollectorReport_AndRemittance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(Slaughter(240m))).IsSuccess);

        await ProveExactlyOnceAsync(w, "SLAUGHTERHOUSE", 240m, "Slaughter");
    }

    // ── The enablement boundary that closes the Collector's legacy writers ──────────────────────────────────

    [SkippableFact]
    public async Task Authority_IsCanonicalOnlyFromTheEnabledDate_AndOnlyForTheEnabledService()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var off = await SeedAsync(enabled: false);
        await using (var ctx = db.CreateContext(off.TenantId))
        {
            var authority = new GovernedCanonicalAuthority(ctx, new FixedTenant(off.TenantId));
            Assert.False(await authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.Tabo, Today));
            Assert.False(await authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.Slaughterhouse, Today));
        }
        await db.ResetAsync();
        var on = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(on.TenantId))
        {
            var authority = new GovernedCanonicalAuthority(ctx, new FixedTenant(on.TenantId));
            Assert.True(await authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.Tabo, Today));
            Assert.True(await authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.Slaughterhouse, Today));
            Assert.False(await authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.Tabo, new DateOnly(1999, 12, 31))); // history is not closed
            Assert.False(await authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.Transportation, Today));
        }
    }

    // ── Shared proof ──────────────────────────────────────────────────────────────────────────────────────

    private async Task ProveExactlyOnceAsync(World w, string rowKey, decimal amount, string natureFragment)
    {
        decimal Income(OfficialMonthlyIncomeDto d) =>
            d.Groups.SelectMany(g => g.Rows).Single(r => r.Key == rowKey).Months[Today.Month - 1].Total;
        async Task<OfficialMonthlyIncomeDto> StatementAsync()
        {
            await using var c = db.CreateContext(w.TenantId);
            var handler = new EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome.GetOfficialMonthlyIncomeQueryHandler(
                c, new EEMOCantilanSDS.Infrastructure.Repositories.LegacyMonthlyIncomeReader(c),
                new Actor(w.AdminId, w.TenantId, "Admin", collector: false), new FixedTenant(w.TenantId), new Clock());
            return (await handler.Handle(new(Today.Year, Today.Month), CancellationToken.None)).Value!;
        }

        Assert.Equal(amount, Income(await StatementAsync()));

        await using var ctx = db.CreateContext(w.TenantId);
        var report = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectorReportQueries(ctx)
            .GetCollectionsAsync(w.CollectorId, Today.AddDays(-1), Today);
        // A governed service is a canonical operation collection (no facility): listed once, under its SRC, and never as a legacy line.
        var mine = (report.OperationCollections ?? []).Where(o => o.OperationName.Contains(natureFragment, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(amount, Assert.Single(mine).Amount);
        Assert.StartsWith("SRC-", mine[0].DocumentNumber);
        Assert.Empty(report.Lines);

        var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(ctx)
            .GetAsync(w.TenantId, Today, Today);
        Assert.Equal(amount, Assert.Single(activity, e => e.Authority == "Canonical").Amount);

        var remit = new RemittanceWorkflow(ctx, new Actor(w.AdminId, w.TenantId, "Admin", collector: false), new FixedTenant(w.TenantId));
        var before = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors.Single(x => x.CollectorId == w.CollectorId);
        Assert.Equal((amount, 0m, amount), (before.Collected, before.Remitted, before.Unremitted));
        var collections = await ctx.Collections.CountAsync();
        var lines = await ctx.CollectionLines.CountAsync();
        var recorded = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.CollectorId, Today, Today.AddDays(-1), Today,
            null, null, amount, "proof", null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var after = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors.Single(x => x.CollectorId == w.CollectorId);
        Assert.Equal((amount, amount, 0m), (after.Collected, after.Remitted, after.Unremitted));
        Assert.Equal((collections, lines), (await ctx.Collections.CountAsync(), await ctx.CollectionLines.CountAsync()));
        Assert.Equal(amount, Income(await StatementAsync()));
    }

    // ── Slaughterhouse itemized approved charges ─────────────────────────────────────────────────────────

    private GovernedServiceWorkflow Governed(AppDbContext ctx, Guid tenantId, string role) =>
        new(ctx, new Actor(Guid.NewGuid(), tenantId, role, collector: false), new FixedTenant(tenantId), new Clock());

    private sealed record Charges(Guid SlaughterFee, Guid AnteMortem, Guid PostMortem, Guid Kilos);

    /// <summary>Seeds an enabled Slaughterhouse, switches it to approved charges as the Head, and has an Admin define the charges.</summary>
    private async Task<(World World, Charges Charges)> SeedItemizedAsync()
    {
        var w = await SeedAsync(enabled: true);
        Guid slaughterFee, ante, post, kilos;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            // Readiness: nothing can be collected by approved charges until at least one charge has a rate in force.
            var head = Governed(ctx, w.TenantId, "SuperAdmin");
            var early = await head.ConfigureAsync(CollectorOperationCodes.Slaughterhouse, new ConfigureGovernedServiceRequest(
                Today.AddDays(-30), GovernedServiceBasis.ApprovedFeeOption, null, null, true, true));
            Assert.False(early.IsSuccess);
            Assert.Equal(ResultStatus.Invalid, early.Status);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var admin = Governed(ctx, w.TenantId, "Admin");   // Head or Admin approve Slaughterhouse charges (IA-050)
            async Task<Guid> Add(string name, int? scope, FeeQuantityBasis basis, decimal amount)
            {
                var added = await admin.AddFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, new AddFeeOptionRequest(
                    name, null, null, null, Today.AddDays(-30), GovernedServiceBasis.FixedAmount, amount, null, scope, basis));
                Assert.True(added.IsSuccess, added.Error);
                return added.Value!.Single(x => x.DisplayName == name).Id;
            }
            slaughterFee = await Add("Slaughter fee", (int)SlaughterAnimalScope.All, FeeQuantityBasis.PerHead, 50m);
            ante = await Add("Ante-mortem inspection", null, FeeQuantityBasis.PerHead, 20m);
            post = await Add("Post-mortem inspection", (int)(SlaughterAnimalScope.Carabao | SlaughterAnimalScope.Cow), FeeQuantityBasis.PerHead, 25m);
            kilos = await Add("Weighing (per kilo)", (int)SlaughterAnimalScope.All, FeeQuantityBasis.PerUnit, 2m);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var head = Governed(ctx, w.TenantId, "SuperAdmin");
            var switched = await head.ConfigureAsync(CollectorOperationCodes.Slaughterhouse, new ConfigureGovernedServiceRequest(
                Today.AddDays(-30), GovernedServiceBasis.ApprovedFeeOption, null, null, true, true));
            Assert.True(switched.IsSuccess, switched.Error);
        }
        return (w, new Charges(slaughterFee, ante, post, kilos));
    }

    private static FeeSchedulePostRequest Itemized(decimal amount, AnimalType animal, int heads, params (Guid Id, int Quantity)[] charges) =>
        new(Guid.NewGuid(), CollectorOperationCodes.Slaughterhouse, Today, amount, Animal: animal, Heads: heads, OwnerName: "Juan Owner",
            Charges: charges.Select(x => new SlaughterChargeSelectionDto(x.Id, x.Quantity)).ToList());

    [SkippableFact]
    public async Task Slaughter_Itemized_TotalsTheSelectedApprovedCharges_OnOneSrc_WithAFrozenBreakdown()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        string src;
        // 3 hogs: slaughter fee 3 x 50 + ante-mortem 3 x 20 + 40 kg weighed x 2 = 290. Post-mortem is not selected: no fee is assumed mandatory.
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = Workflow(ctx, w.TenantId, w.CollectorId);
            var terms = (await workflow.GetSlaughterTermsAsync(null)).Value!;
            Assert.True(terms.Itemized);
            Assert.Equal(4, terms.Charges.Count);
            Assert.All(terms.Charges, x => Assert.Equal(GovernedServiceBasis.FixedAmount, x.Basis));   // a collector is never offered a rate to type

            var request = Itemized(290m, AnimalType.Hog, 3, (c.SlaughterFee, 3), (c.AnteMortem, 3), (c.Kilos, 40));
            var quote = await workflow.QuoteSlaughterAsync(request);
            Assert.True(quote.IsSuccess, quote.Error);
            Assert.Equal(290m, quote.Value!.Amount);
            Assert.Equal(3, quote.Value.Items!.Count);

            var posted = await workflow.PostMobileAsync(request);
            Assert.True(posted.IsSuccess, posted.Error);
            src = posted.Value!.ReferenceCode;
            Assert.Equal(290m, posted.Value.Amount);
            Assert.Equal(src, (await workflow.PostMobileAsync(request)).Value!.ReferenceCode);   // a replay is the same collection
        }
        await using var verify = db.CreateContext(w.TenantId);
        var collection = Assert.Single(await verify.Collections.ToListAsync());
        var lines = await verify.CollectionLines.Where(x => x.CollectionId == collection.Id).ToListAsync();
        Assert.Equal((3, 290m), (lines.Count, lines.Sum(x => x.Amount)));                            // one SRC, one line per charge
        Assert.Empty(await verify.SlaughterTransactions.ToListAsync());                              // no legacy twin
        Assert.Contains(lines, x => x.Amount == 80m && x.CalculationSnapshot!.Contains("\"quantity\":40"));

        // The office reads one transaction with its itemized breakdown; heads and money are counted once.
        var repo = new EEMOCantilanSDS.Infrastructure.Repositories.SlaughterRepository(verify);
        var overview = await repo.GetOverviewAsync(Today.Year, Today.Month);
        Assert.Equal((1, 3, 290m, 3), (overview.TotalTransactions, overview.TotalHeads, overview.TotalCollected, overview.HogCount));
        var row = Assert.Single(Assert.Single(await repo.GetGroupedTransactionsByMonthAsync(Today.Year, Today.Month)).LatestTransactions);
        Assert.Equal((src, 290m), (row.ReferenceCode, row.TotalAmount));
        Assert.Equal(3, row.Items!.Count);
        Assert.Contains(row.Items, i => i.Label == "Weighing (per kilo)" && i.Quantity == 40m && i.UnitAmount == 2m && i.Amount == 80m);
        var profile = (await repo.GetClientProfileAsync("Juan Owner"))!;                              // the client record carries the SRC and its breakdown
        Assert.Equal((1, 3, 290m), (profile.TotalTransactions, profile.TotalHeads, profile.TotalCollected));
        Assert.Equal((src, 3), (profile.Transactions.Single().ORNumber, profile.Transactions.Single().Items!.Count));

        var activity = Assert.Single((await Governed(verify, w.TenantId, "Admin").GetActivityAsync(
            CollectorOperationCodes.Slaughterhouse, Today, Today)).Value!);
        Assert.Equal(290m, activity.Amount);
        Assert.Equal(3, activity.Items!.Count);
        var totals = (await Governed(verify, w.TenantId, "Admin").GetFeeOptionTotalsAsync(CollectorOperationCodes.Slaughterhouse, Today, Today)).Value!;
        Assert.Equal(290m, totals.Sum(x => x.Amount));                                               // the by-charge breakdown adds up to the collection

        // Remittance moves only collected / remitted / unremitted; an itemized collection is counted once.
        var remit = new RemittanceWorkflow(verify, new Actor(w.AdminId, w.TenantId, "Admin", collector: false), new FixedTenant(w.TenantId));
        var position = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors.Single(x => x.CollectorId == w.CollectorId);
        Assert.Equal((290m, 0m, 290m), (position.Collected, position.Remitted, position.Unremitted));
    }

    [SkippableFact]
    public async Task Slaughter_Itemized_RefusesEverythingTheHeadHasNotApproved_AndNeverPricesByCollector()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var workflow = Workflow(ctx, w.TenantId, w.CollectorId);

        Assert.False((await workflow.PostMobileAsync(Slaughter(240m))).IsSuccess);                                          // per-head total: itemized required
        Assert.False((await workflow.PostMobileAsync(Itemized(250m, AnimalType.Hog, 3, (c.SlaughterFee, 3)))).IsSuccess);  // 150 is the total; amount must match
        Assert.False((await workflow.PostMobileAsync(Itemized(75m, AnimalType.Hog, 3, (c.PostMortem, 3)))).IsSuccess);     // post-mortem applies to carabao / cow only
        Assert.False((await workflow.PostMobileAsync(Itemized(100m, AnimalType.Hog, 3, (c.SlaughterFee, 2)))).IsSuccess);  // per-head quantity is the head count
        Assert.False((await workflow.PostMobileAsync(Itemized(50m, AnimalType.Hog, 3, (Guid.NewGuid(), 3)))).IsSuccess);   // not an approved charge
        Assert.False((await workflow.PostMobileAsync(Itemized(1m, AnimalType.Hog, 3, (c.Kilos, 0)))).IsSuccess);           // a counted quantity is required
        Assert.False((await workflow.PostMobileAsync(Itemized(300m, AnimalType.Hog, 3,
            (c.SlaughterFee, 3), (c.SlaughterFee, 3)))).IsSuccess);                                                         // a charge once per transaction
        Assert.Empty(await ctx.Collections.ToListAsync());
        Assert.Empty(await ctx.CollectionLines.ToListAsync());

        // A large animal does take the post-mortem charge, at its approved rate.
        var cow = await workflow.PostMobileAsync(Itemized(2 * (50m + 20m + 25m), AnimalType.Cow, 2,
            (c.SlaughterFee, 2), (c.AnteMortem, 2), (c.PostMortem, 2)));
        Assert.True(cow.IsSuccess, cow.Error);
        Assert.Equal(190m, cow.Value!.Amount);
    }

    [SkippableFact]
    public async Task Slaughter_Itemized_AChangedRateAppliesFromItsDateOnly_AndNeverReprices_APostedReceipt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileAsync(Itemized(150m, AnimalType.Hog, 3, (c.SlaughterFee, 3)))).IsSuccess);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var admin = Governed(ctx, w.TenantId, "Admin");
            Assert.True((await admin.ScheduleFeeOptionRateAsync(CollectorOperationCodes.Slaughterhouse, c.SlaughterFee,
                new ScheduleFeeOptionRateRequest(Today.AddDays(1), GovernedServiceBasis.FixedAmount, 60m, null))).IsSuccess);
            // A rate a collector would type is refused outright: Slaughterhouse charges are fixed approved rates.
            Assert.False((await admin.ScheduleFeeOptionRateAsync(CollectorOperationCodes.Slaughterhouse, c.SlaughterFee,
                new ScheduleFeeOptionRateRequest(Today.AddDays(2), GovernedServiceBasis.DirectApprovedAmount, null, 100m))).IsSuccess);
        }
        await using var verify = db.CreateContext(w.TenantId);
        var workflow = Workflow(verify, w.TenantId, w.CollectorId);
        Assert.Equal(150m, (await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Hog, 3, (c.SlaughterFee, 3)))).Value!.Amount);   // today still 50 x 3
        Assert.Equal(150m, (await verify.CollectionLines.SingleAsync()).Amount);                                                        // the posted receipt is untouched
    }

    private FeeScheduleCollectionWorkflow Office(AppDbContext ctx, Guid tenantId, string role = "Admin") =>
        new(ctx, new Actor(Guid.NewGuid(), tenantId, role, collector: false), new FixedTenant(tenantId),
            new FeeRateResolver(ctx), new MarketDay(Today.DayOfWeek), new Clock());

    [SkippableFact]
    public async Task Slaughter_Itemized_OfficeRecordsWithTheServersPricing_OnOneSrc_AndOnlyTheOffice()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var office = Office(ctx, w.TenantId);
        var terms = (await office.GetOfficeSlaughterTermsAsync(null)).Value!;
        Assert.True(terms.Itemized);
        Assert.Equal(4, terms.Charges.Count);
        Assert.True(await office.IsSlaughterItemizedAsync(Today));
        Assert.False(await office.IsSlaughterItemizedAsync(Today.AddDays(-60)));   // before the switch the earlier basis (and its office writer) applies

        // A collector never reaches the office endpoints, and the office never reaches the collector ones.
        var collector = Workflow(ctx, w.TenantId, w.CollectorId);
        var request = Itemized(0m, AnimalType.Carabao, 2, (c.SlaughterFee, 2), (c.PostMortem, 2));
        Assert.Equal(ResultStatus.Forbidden, (await collector.QuoteOfficeSlaughterAsync(request)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await office.QuoteSlaughterAsync(request)).Status);

        var quote = await office.QuoteOfficeSlaughterAsync(request);
        Assert.True(quote.IsSuccess, quote.Error);
        Assert.Equal(150m, quote.Value!.Amount);                                       // 2 x 50 + 2 x 25
        Assert.Equal(ResultStatus.Conflict, (await office.PostOfficeSlaughterAsync(request with { ClientOperationId = Guid.NewGuid(), ReceivedAmount = 149m })).Status);
        var posted = await office.PostOfficeSlaughterAsync(request with { ReceivedAmount = 150m });
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(posted.Value!.ReferenceCode, (await office.PostOfficeSlaughterAsync(request with { ReceivedAmount = 150m })).Value!.ReferenceCode);
        var collection = await ctx.Collections.SingleAsync();
        Assert.Null(collection.CollectorId);                                            // recorded at the office: no collector attribution
        Assert.Equal(2, await ctx.CollectionLines.CountAsync(x => x.CollectionId == collection.Id));
        Assert.Equal(0, await ctx.SlaughterTransactions.CountAsync());                  // no second (legacy) record of the same payment

        // The same operation id with different charges is a conflict, never a second posting.
        var other = request with { ReceivedAmount = 100m, Charges = [new SlaughterChargeSelectionDto(c.SlaughterFee, 2)] };
        Assert.Equal(ResultStatus.Conflict, (await office.PostOfficeSlaughterAsync(other)).Status);
        Assert.Equal(1, await ctx.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Slaughter_Itemized_QueuedChargesSyncOnce_AnOldPayloadIsRefused_AndAReversalKeepsTheEvidence()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var actor = new Actor(w.CollectorId, w.TenantId, "Collector", collector: true);
        var sync = new EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections.SyncOfflineCollectionsCommandHandler(
            null!, null!, actor, feeScheduleWorkflow: Workflow(ctx, w.TenantId, w.CollectorId));

        // The Mobile queue's payload: facts and the selected charges; the amount is the total the device showed.
        var operation = Guid.NewGuid();
        var queued = new EEMOCantilanSDS.Application.Dtos.Mobile.SyncOfflineOperationDto(operation,
            EEMOCantilanSDS.Application.Dtos.Mobile.OfflineOperationKind.FeeScheduleCollection, Today,
            OperationCode: CollectorOperationCodes.Slaughterhouse, OwnerName: "Juan Owner", AnimalType: AnimalType.Hog, NumberOfHeads: 3,
            ReceivedAmount: 210m, SlaughterCharges: [new SlaughterChargeSelectionDto(c.SlaughterFee, 3), new SlaughterChargeSelectionDto(c.AnteMortem, 3)]);
        var first = Assert.Single((await sync.Handle(new([queued]), default)).Value!.Results);
        Assert.Equal(EEMOCantilanSDS.Application.Dtos.Mobile.SyncResultStatus.Synced, first.Status);
        var again = Assert.Single((await sync.Handle(new([queued]), default)).Value!.Results);
        Assert.Equal((first.ReferenceCode, first.CollectionId), (again.ReferenceCode, again.CollectionId));
        Assert.Equal(1, await ctx.Collections.CountAsync());

        // A payload from an app that cannot select charges is refused with the reason; nothing is posted.
        var old = queued with { ClientOperationId = Guid.NewGuid(), SlaughterCharges = null };
        var refused = Assert.Single((await sync.Handle(new([old]), default)).Value!.Results);
        Assert.Equal(EEMOCantilanSDS.Application.Dtos.Mobile.SyncResultStatus.Rejected, refused.Status);
        Assert.Contains("approved charges", refused.Message);
        Assert.Equal(1, await ctx.Collections.CountAsync());

        // A stale total (the rate moved while the device was offline) is refused rather than posted at the old price.
        var stale = queued with { ClientOperationId = Guid.NewGuid(), ReceivedAmount = 200m };
        Assert.Equal(EEMOCantilanSDS.Application.Dtos.Mobile.SyncResultStatus.Rejected,
            Assert.Single((await sync.Handle(new([stale]), default)).Value!.Results).Status);

        // Correction is remove-and-record-again: the original Collection and SRC stay, a reversal records the effect.
        var corrections = new MobileCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), null!, null!, new CollectionActivityReader(ctx), null!,
            actor, new FixedTenant(w.TenantId), new Clock());
        var original = await ctx.Collections.AsNoTracking().SingleAsync();
        var removed = await corrections.RemoveAsync(new(Guid.NewGuid(), original.Id, new(MobileCorrectionReason.WrongAmount)));
        Assert.True(removed.IsSuccess, removed.Error);
        var kept = await ctx.Collections.AsNoTracking().SingleAsync();
        Assert.Equal((original.ReferenceCode, original.TotalAmount, original.CollectorId), (kept.ReferenceCode, kept.TotalAmount, kept.CollectorId));
        var reversal = await ctx.CollectionCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(-210m, reversal.FinancialEffectAmount);
        Assert.Equal(original.Id, reversal.OriginalCollectionId);
        // ...and the same collection cannot be corrected twice.
        Assert.False((await corrections.RemoveAsync(new(Guid.NewGuid(), original.Id, new(MobileCorrectionReason.WrongAmount)))).IsSuccess);
    }

    [SkippableFact]
    public async Task Slaughter_CollectorCash_CanBeReversedByTheHeadOnce_AndTheSrcAndEvidenceStay()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var collector = new Actor(w.CollectorId, w.TenantId, "Collector", collector: true);
        var sync = new EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections.SyncOfflineCollectionsCommandHandler(
            null!, null!, collector, feeScheduleWorkflow: Workflow(ctx, w.TenantId, w.CollectorId));
        var queued = new EEMOCantilanSDS.Application.Dtos.Mobile.SyncOfflineOperationDto(Guid.NewGuid(),
            EEMOCantilanSDS.Application.Dtos.Mobile.OfflineOperationKind.FeeScheduleCollection, Today,
            OperationCode: CollectorOperationCodes.Slaughterhouse, OwnerName: "Juan Owner", AnimalType: AnimalType.Hog, NumberOfHeads: 3,
            ReceivedAmount: 210m, SlaughterCharges: [new SlaughterChargeSelectionDto(c.SlaughterFee, 3), new SlaughterChargeSelectionDto(c.AnteMortem, 3)]);
        Assert.Equal(EEMOCantilanSDS.Application.Dtos.Mobile.SyncResultStatus.Synced, Assert.Single((await sync.Handle(new([queued]), default)).Value!.Results).Status);
        var original = await ctx.Collections.AsNoTracking().SingleAsync();

        var head = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), new Actor(Guid.NewGuid(), w.TenantId, "SuperAdmin", collector: false),
            new FixedTenant(w.TenantId), new Clock());
        // A wrong direct collection can be reversed by the Head even though a collector recorded it, while it is today's and unremitted.
        Assert.True((await head.ReviewAsync(original.Id)).Value!.Eligibility.CanCorrect);
        var done = await head.ReverseAsync(new(Guid.NewGuid(), original.Id, "Wrong animal keyed"));
        Assert.True(done.IsSuccess, done.Error);

        var kept = await ctx.Collections.AsNoTracking().SingleAsync();
        Assert.Equal((original.ReferenceCode, original.TotalAmount, original.CollectorId), (kept.ReferenceCode, kept.TotalAmount, kept.CollectorId));
        var reversal = await ctx.CollectionCorrections.AsNoTracking().SingleAsync();
        Assert.Equal((original.Id, -210m), (reversal.OriginalCollectionId, reversal.FinancialEffectAmount));
        // Once only, by either hand.
        Assert.Equal("AlreadyCorrected", (await head.ReverseAsync(new(Guid.NewGuid(), original.Id, "Again"))).Error);
        var collectorCorrections = new MobileCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), null!, null!, new CollectionActivityReader(ctx), null!,
            collector, new FixedTenant(w.TenantId), new Clock());
        Assert.False((await collectorCorrections.RemoveAsync(new(Guid.NewGuid(), original.Id, new(MobileCorrectionReason.WrongAmount)))).IsSuccess);
    }

    [SkippableFact]
    public async Task Slaughter_ApprovedAnimal_NeedsNoPerHeadRate_OnlyWhileTheOfficeCollectsByCharges()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var animals = new EEMOCantilanSDS.Application.Common.Slaughterhouse.ApprovedSlaughterAnimalWorkflow(
                ctx, new Actor(Guid.NewGuid(), w.TenantId, "Admin", collector: false), new FixedTenant(w.TenantId),
                new EEMOCantilanSDS.Infrastructure.Repositories.SlaughterAnimalLabelProvider(ctx));
            // Per-head basis: the rate is still required.
            Assert.False((await animals.SaveAsync(new("Chicken", null))).IsSuccess);
            Assert.True((await animals.SaveAsync(new("Chicken", 12m))).IsSuccess);
        }
        var (w2, c) = await SeedItemizedAsync();
        await using (var ctx = db.CreateContext(w2.TenantId))
        {
            var animals = new EEMOCantilanSDS.Application.Common.Slaughterhouse.ApprovedSlaughterAnimalWorkflow(
                ctx, new Actor(Guid.NewGuid(), w2.TenantId, "Admin", collector: false), new FixedTenant(w2.TenantId),
                new EEMOCantilanSDS.Infrastructure.Repositories.SlaughterAnimalLabelProvider(ctx));
            var saved = await animals.SaveAsync(new("Duck", null));
            Assert.True(saved.IsSuccess, saved.Error);
            Assert.Equal(0m, saved.Value!.RatePerHead);
            // The charges price it; the legacy per-head path never charges an animal that has no per-head rate.
            var workflow = Workflow(ctx, w2.TenantId, w2.CollectorId);
            var request = new FeeSchedulePostRequest(Guid.NewGuid(), CollectorOperationCodes.Slaughterhouse, Today, 0m, Animal: AnimalType.Other,
                CustomAnimalName: "Duck", Heads: 2, OwnerName: "Juan Owner", Charges: [new SlaughterChargeSelectionDto(c.SlaughterFee, 2)]);
            Assert.Equal(100m, (await workflow.QuoteSlaughterAsync(request)).Value!.Amount);
        }
    }

    [SkippableFact]
    public async Task Slaughter_ReferencePresets_AreOnlyForTheReferenceTenant_AndPrefillNothingUntilApproved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var other = await SeedAsync(enabled: true);
        await using (var ctx = db.CreateContext(other.TenantId))
            Assert.Empty((await Governed(ctx, other.TenantId, "Admin").GetSlaughterChargePresetsAsync()).Value!);   // another LGU's rates are its own to state

        await db.ResetAsync();
        var w = await SeedAsync(enabled: true, referenceTenant: true);
        await using var verify = db.CreateContext(w.TenantId);
        var admin = Governed(verify, w.TenantId, "Admin");
        var presets = (await admin.GetSlaughterChargePresetsAsync()).Value!;
        Assert.Equal(10, presets.Count);                                                                  // no Hog post-mortem, nothing for Other animals
        Assert.Equal((250m, 365m), (presets.Where(x => x.Group == "Hog").Sum(x => x.ReferenceAmount),
            presets.Where(x => x.Group == "Carabao / Cow").Sum(x => x.ReferenceAmount)));
        Assert.Empty((await admin.GetFeeOptionsAsync(CollectorOperationCodes.Slaughterhouse)).Value!);     // reading presets defines nothing
        Assert.Equal(0, await verify.GovernedServiceSettings.CountAsync(x => x.Basis == GovernedServiceBasis.ApprovedFeeOption));

        // The Head/Admin reviews and adds each: the same name carries different rates for different animals.
        foreach (var p in presets)
        {
            var added = await admin.AddFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, new AddFeeOptionRequest(
                p.Name, null, null, null, Today.AddDays(-30), GovernedServiceBasis.FixedAmount, p.ReferenceAmount, null, p.ApplicableAnimals, p.QuantityBasis));
            Assert.True(added.IsSuccess, $"{p.Group} {p.Name}: {added.Error}");
        }
        // ...but never two offered definitions of one name for the same animal (here: all animals overlaps both).
        var overlap = await admin.AddFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, new AddFeeOptionRequest(
            "slaughter fee", null, null, null, Today, GovernedServiceBasis.FixedAmount, 10m, null, null, FeeQuantityBasis.PerHead));
        Assert.Equal(ResultStatus.Conflict, overlap.Status);
        var options = (await admin.GetFeeOptionsAsync(CollectorOperationCodes.Slaughterhouse)).Value!;
        Assert.Equal(10, options.Count);
        Assert.True((await Governed(verify, w.TenantId, "SuperAdmin").ConfigureAsync(CollectorOperationCodes.Slaughterhouse,
            new ConfigureGovernedServiceRequest(Today.AddDays(-30), GovernedServiceBasis.ApprovedFeeOption, null, null, true, true))).IsSuccess);

        // The documented per-head packages are reproduced exactly by the selected charges, and no hog charge reaches a carabao.
        var workflow = Workflow(verify, w.TenantId, w.CollectorId);
        Guid Id(string name, int scope) => options.Single(x => x.DisplayName == name && x.ApplicableAnimals == scope).Id;
        var hog = (int)SlaughterAnimalScope.Hog;
        var large = (int)(SlaughterAnimalScope.Carabao | SlaughterAnimalScope.Cow);
        var hogCharges = options.Where(x => x.ApplicableAnimals == hog).Select(x => (x.Id, 1)).ToArray();
        var largeCharges = options.Where(x => x.ApplicableAnimals == large).Select(x => (x.Id, 1)).ToArray();
        Assert.Equal(250m, (await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Hog, 1, hogCharges))).Value!.Amount);
        Assert.Equal(750m, (await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Hog, 3, hogCharges.Select(x => (x.Id, 3)).ToArray()))).Value!.Amount);
        Assert.Equal(365m, (await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Carabao, 1, largeCharges))).Value!.Amount);
        Assert.Equal(365m, (await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Cow, 1, largeCharges))).Value!.Amount);
        Assert.False((await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Carabao, 1, (Id("Slaughter Fee", hog), 1)))).IsSuccess);
        Assert.False((await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Hog, 1, (Id("Post-mortem Inspection", large), 1)))).IsSuccess);   // no hog post-mortem
        Assert.False((await workflow.QuoteSlaughterAsync(Itemized(0m, AnimalType.Other, 1, hogCharges))).IsSuccess);                                  // nothing is invented for other animals
        Assert.Equal(0, await verify.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Slaughter_Charge_CanBeReinstated_AfterRetiring_WithItsRatesAndHistoryIntact()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var admin = Governed(ctx, w.TenantId, "Admin");
        Assert.False((await admin.ReinstateFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, c.AnteMortem)).IsSuccess);   // nothing to undo
        Assert.True((await admin.RetireFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, c.AnteMortem, new RetireFeeOptionRequest(Today.AddDays(1)))).IsSuccess);
        var back = await admin.ReinstateFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, c.AnteMortem);
        Assert.True(back.IsSuccess, back.Error);
        var charge = back.Value!.Single(x => x.Id == c.AnteMortem);
        Assert.Null(charge.RetiredFrom);
        Assert.Equal(20m, charge.Amount);                                                  // the approved rate is untouched
        Assert.Equal(ResultStatus.Forbidden, (await Governed(ctx, w.TenantId, "Collector").ReinstateFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, c.AnteMortem)).Status);
    }

    [SkippableFact]
    public async Task Slaughter_Charges_AreManagedByHeadAndAdminOnly_AndAdminCannotTouchOtherServices()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (w, c) = await SeedItemizedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var collector = new GovernedServiceWorkflow(ctx, new Actor(w.CollectorId, w.TenantId, "Collector", collector: true), new FixedTenant(w.TenantId), new Clock());
        Assert.False((await collector.AddFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, new AddFeeOptionRequest(
            "Extra", null, null, null, Today, GovernedServiceBasis.FixedAmount, 5m, null))).IsSuccess);
        var admin = Governed(ctx, w.TenantId, "Admin");
        Assert.Equal(ResultStatus.Forbidden, (await admin.AddFeeOptionAsync(CollectorOperationCodes.MarketFees, new AddFeeOptionRequest(
            "Comfort", null, null, null, Today, GovernedServiceBasis.FixedAmount, 5m, null))).Status);
        Assert.False((await admin.AddFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, new AddFeeOptionRequest(
            "Typed rate", null, null, null, Today, GovernedServiceBasis.DirectApprovedAmount, null, 100m))).IsSuccess);   // no collector-entered rate

        // Relabel and re-scope an existing charge without touching its rates.
        var updated = await admin.UpdateFeeOptionAsync(CollectorOperationCodes.Slaughterhouse, c.AnteMortem,
            new UpdateFeeOptionRequest("Ante-mortem", (int)SlaughterAnimalScope.Hog, FeeQuantityBasis.PerHead));
        Assert.True(updated.IsSuccess, updated.Error);
        var charge = updated.Value!.Single(x => x.Id == c.AnteMortem);
        Assert.Equal(((int)SlaughterAnimalScope.Hog, 20m), (charge.ApplicableAnimals, charge.Amount));
    }
}
