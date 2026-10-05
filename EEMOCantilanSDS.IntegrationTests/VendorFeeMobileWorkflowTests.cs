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
/// Collector Mobile Fish / Meat Vendor Fee on PostgreSQL (Phase 2.3). The authority is the existing obligation account (the NPM
/// Fish/Meat stall's explicit Payor and approved monthly amount); the writer only collects against an assessed period, never more
/// than its balance, with no physical serial and an SRC returned, idempotently, counted once everywhere.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VendorFeeMobileWorkflowTests(PostgresFixture db)
{
    // The former monthly model remains covered as historical, pre-IA-064 behavior.
    private static readonly DateOnly Today = FishMeatVendorFeeRules.DirectEffectiveDate.AddDays(-1);
    private static readonly DateOnly Effective = new(2000, 1, 1);

    private sealed record World(Guid TenantId, Guid AdminId, Guid CollectorId, Guid AccountId);

    private sealed class Actor(Guid userId, Guid tenantId, string role, bool collector) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => collector ? "mobile-collector" : "office-admin";
        public string? Role => role;
        public Guid? CollectorId => collector ? userId : null;
        public string? MunicipalityCode => "vendor-fee";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private CollectionComposerWorkflow Workflow(AppDbContext ctx, Guid tenantId, Guid collectorId, string role = "Collector") =>
        new(ctx, new Actor(collectorId, tenantId, role, collector: true), new FixedTenant(tenantId), new Clock());

    private async Task<World> SeedAsync(bool withPolicy = true, bool withRate = true, bool assignNpm = true)
    {
        var tenant = Municipality.Create($"vf-{Guid.NewGuid():N}"[..12], "Vendor Fee", "Province", MunicipalityStatus.Active,
            tenantCode: $"vendorfee-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", $"vf-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        await using var ctx = db.CreateContext(tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Public Market", "NPM", municipalityId: tenant.Id);
        var stall = Stall.Create(npm.Id, "F-01", 0m, ApplicableFees.BaseRental, MarketSection.FishSection, municipalityId: tenant.Id);
        var payor = Payor.Create(tenant.Id, "Maria Fish Vendor", BusinessPayorKind.Person, "test");
        ctx.AddRange(npm, stall, payor);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.FishMeatVendorFee, tenant.Id);
        ctx.Add(classification);
        if (withPolicy)
            ctx.Add(RevenueClassificationPolicy.Create(classification.Id, Effective, "Fish/Meat Vendor Fee", RevenueInstrumentType.OfficialReceipt, tenant.Id));
        var account = ObligationAccount.Create(tenant.Id, ObligationKind.FishMeatVendorFee, payor.Id, stall.Id, "Fish stall F-01",
            null, null, new DateOnly(Today.Year, Today.Month, 1), "head");
        ctx.Add(account);
        if (withRate) ctx.Add(ObligationRate.Create(tenant.Id, account.Id, new DateOnly(Today.Year, Today.Month, 1), 900m, "head"));
        if (assignNpm) ctx.Add(CollectorFacilityAssignment.Create(collector.Id, npm.Id, FacilityCode.NPM));
        await ctx.SaveChangesAsync();
        return new World(tenant.Id, Guid.NewGuid(), collector.Id, account.Id);
    }

    private static MobileObligationPostRequest Request(World w, decimal amount, Guid? op = null) =>
        new(op ?? Guid.NewGuid(), w.AccountId, Today.Year, Today.Month, amount, Today);

    [SkippableFact]
    public async Task VendorFee_PostsAgainstTheApprovedPeriod_WithNoSerial_ReturnsSrc_ReplaysTheSame_AndIsPartialCapable()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var dues = (await Workflow(ctx, w.TenantId, w.CollectorId).GetMobileVendorFeeDuesAsync()).Value!;
            var due = Assert.Single(dues);
            Assert.Equal((900m, 0m, 900m), (due.AssessedAmount, due.SettledAmount, due.OutstandingAmount));   // the office's approved amount
        }

        var first = Request(w, 300m);
        string src;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var posted = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(first);
            Assert.True(posted.IsSuccess, posted.Error);
            src = posted.Value!.ReferenceCode;
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", src);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var replay = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(first);
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((src, true), (replay.Value!.ReferenceCode, replay.Value.ReturnedExistingOutcome));
        }

        await using var verify = db.CreateContext(w.TenantId);
        var collection = await verify.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal((300m, w.CollectorId), (collection.TotalAmount, collection.CollectorId!.Value));
        Assert.Equal(CollectionSourceKind.ObligationPeriod, Assert.Single(Assert.Single(collection.Lines).Allocations).SourceKind);
        Assert.Empty(await verify.AccountableDocuments.ToListAsync());                      // no physical form, no typed serial
        Assert.Empty(await verify.DailyCollections.ToListAsync());                          // a distinct source: never NPM daily
        var remaining = (await Workflow(verify, w.TenantId, w.CollectorId).GetMobileVendorFeeDuesAsync()).Value!;
        Assert.Equal(600m, Assert.Single(remaining).OutstandingAmount);                     // partial settles the balance, not the obligation
    }

    [SkippableFact]
    public async Task VendorFee_IsRefusedWithoutWriting_ForOverAmount_NoRate_NoPolicy_Unassigned_Admin_AndAnIdempotencyConflict()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            Assert.False((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(Request(w, 900.01m))).IsSuccess);
            Assert.False((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(Request(w, 0m))).IsSuccess);
            Assert.Equal(ResultStatus.Forbidden, (await Workflow(ctx, w.TenantId, Guid.NewGuid()).PostMobileObligationAsync(Request(w, 100m))).Status);
            Assert.Equal(ResultStatus.Forbidden, (await Workflow(ctx, w.TenantId, w.CollectorId, "Admin").PostMobileObligationAsync(Request(w, 100m))).Status);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        var op = Guid.NewGuid();
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(Request(w, 100m, op))).IsSuccess);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var conflict = await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(Request(w, 200m, op));
            Assert.Equal(ResultStatus.Conflict, conflict.Status);
            Assert.Single(await ctx.Collections.ToListAsync());
        }

        await db.ResetAsync();
        var noRate = await SeedAsync(withRate: false);
        await using (var ctx = db.CreateContext(noRate.TenantId))
        {
            Assert.False((await Workflow(ctx, noRate.TenantId, noRate.CollectorId).PostMobileObligationAsync(Request(noRate, 100m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        await db.ResetAsync();
        var noPolicy = await SeedAsync(withPolicy: false);
        await using (var ctx = db.CreateContext(noPolicy.TenantId))
        {
            Assert.False((await Workflow(ctx, noPolicy.TenantId, noPolicy.CollectorId).PostMobileObligationAsync(Request(noPolicy, 100m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        await db.ResetAsync();
        var unassigned = await SeedAsync(assignNpm: false);
        await using (var ctx = db.CreateContext(unassigned.TenantId))
            Assert.Equal(ResultStatus.Forbidden, (await Workflow(ctx, unassigned.TenantId, unassigned.CollectorId).PostMobileObligationAsync(Request(unassigned, 100m))).Status);
    }

    [SkippableFact]
    public async Task VendorFee_RefusesAnotherTenantsAccount()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedAsync();
        var b = await SeedAsync();
        await using var ctx = db.CreateContext(b.TenantId);
        // B's collector, inside B, naming A's account: not found in B, nothing written in either tenant.
        var refused = await Workflow(ctx, b.TenantId, b.CollectorId).PostMobileObligationAsync(Request(a, 100m));
        Assert.False(refused.IsSuccess);
        Assert.Empty(await ctx.Collections.ToListAsync());
        await using var other = db.CreateContext(a.TenantId);
        Assert.Empty(await other.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task VendorFee_IsCountedOnceInMonthlyIncome_CollectorReport_ActivityAndRemittance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Workflow(ctx, w.TenantId, w.CollectorId).PostMobileObligationAsync(Request(w, 400m))).IsSuccess);

        decimal Income(OfficialMonthlyIncomeDto d) =>
            d.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "FISH_MEAT_VENDOR_FEE").Months[Today.Month - 1].Total;
        async Task<OfficialMonthlyIncomeDto> StatementAsync()
        {
            await using var c = db.CreateContext(w.TenantId);
            var handler = new EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome.GetOfficialMonthlyIncomeQueryHandler(
                c, new EEMOCantilanSDS.Infrastructure.Repositories.LegacyMonthlyIncomeReader(c),
                new Actor(w.AdminId, w.TenantId, "Admin", collector: false), new FixedTenant(w.TenantId), new Clock());
            return (await handler.Handle(new(Today.Year, Today.Month), CancellationToken.None)).Value!;
        }
        Assert.Equal(400m, Income(await StatementAsync()));

        await using var verify = db.CreateContext(w.TenantId);
        var report = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectorReportQueries(verify)
            .GetCollectionsAsync(w.CollectorId, Today.AddDays(-1), Today);
        var mine = (report.OperationCollections ?? []).Where(o => o.OperationName.Contains("Vendor Fee", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(400m, Assert.Single(mine).Amount);
        Assert.StartsWith("SRC-", mine[0].DocumentNumber);
        Assert.Empty(report.Lines);
        var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(verify).GetAsync(w.TenantId, Today, Today);
        Assert.Equal(400m, Assert.Single(activity, e => e.Authority == "Canonical").Amount);

        var remit = new RemittanceWorkflow(verify, new Actor(w.AdminId, w.TenantId, "Admin", collector: false), new FixedTenant(w.TenantId));
        var before = (await remit.GetPositionAsync(Today.AddDays(-1), Today)).Value!.Collectors.Single(x => x.CollectorId == w.CollectorId);
        Assert.Equal((400m, 0m, 400m), (before.Collected, before.Remitted, before.Unremitted));
        var recorded = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.CollectorId, Today, Today.AddDays(-1), Today, null, null, 400m, "proof", null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        Assert.Equal(1, await verify.Collections.CountAsync());
        Assert.Equal(400m, Income(await StatementAsync()));                                // a remittance never moves income
    }
}
