using EEMOCantilanSDS.Application.Common.Fees;
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
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
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
    private async Task<World> SeedAsync(bool enabled, bool taboRate = true, bool hogRate = true)
    {
        var tenant = Municipality.Create($"fs-{Guid.NewGuid():N}"[..12], "Fee Schedule", "Province", MunicipalityStatus.Active,
            tenantCode: $"feesched-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
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
}
