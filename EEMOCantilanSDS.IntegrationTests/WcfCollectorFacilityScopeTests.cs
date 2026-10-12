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
/// A Collector's WCF authority is the facilities they are explicitly assigned to, never NPM by default: a TCC-only Collector cannot
/// discover, quote or collect NPM Water, and an NPM-only Collector cannot reach TCC. The one preserved legacy rule is that a Collector
/// with the WCF operation assignment and no facility assignment at all keeps the NPM context. Another tenant's sources, a missing
/// WCF operation assignment and a forged post that names a source outside the scope are all refused.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WcfCollectorFacilityScopeTests(PostgresFixture db)
{
    private static DateOnly Today => PhilippineTime.Today;

    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "field-collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "scope-x";
        public Guid? MunicipalityId => tenantId;
    }
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid municipalityId) { } }
    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private sealed record World(Guid TenantId, Guid Head, Guid NpmStall, Guid TccStall,
        Guid OpOnly, Guid OpTcc, Guid OpNpm, Guid OpBoth, Guid NoOp);

    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"sc-{Guid.NewGuid():N}"[..12], "Scope X", "Province", MunicipalityStatus.Active,
            tenantCode: $"scopx-{Guid.NewGuid():N}"[..24].ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Public Market", "NPM", archetype: BillingArchetype.DailyStall, municipalityId: tenant.Id);
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: tenant.Id);
        ctx.AddRange(npm, tcc);
        var from = new DateOnly(Today.Year, Today.Month, 1).AddYears(-1);
        Stall Space(Facility f, string no, ApplicableFees fees, string occupant)
        {
            var stall = Stall.Create(f.Id, no, 900m, fees, null, municipalityId: tenant.Id);
            ctx.Add(stall); ctx.Add(Contract.Create(stall.Id, occupant, occupant, from, 5, 900m, createdBy: "test")); return stall;
        }
        var npmStall = Space(npm, "V-1", ApplicableFees.BaseRental, "Vic Vendor");
        var tccStall = Space(tcc, "W-1", ApplicableFees.BaseRental | ApplicableFees.Water, "Wilma Water");
        var ecf = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant.Id);
        var wcf = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenant.Id);
        ctx.AddRange(ecf, wcf,
            RevenueClassificationPolicy.Create(ecf.Id, new DateOnly(2000, 1, 1), "Electricity Consumption Fees", RevenueInstrumentType.OfficialReceipt, tenant.Id),
            RevenueClassificationPolicy.Create(wcf.Id, new DateOnly(2000, 1, 1), "Water Consumption Fees", RevenueInstrumentType.CashTicket, tenant.Id));

        CollectorUser Collector(string name, bool wcfOperation, params Facility[] facilities)
        {
            var user = CollectorUser.Create(name, "C-" + name, name.ToLowerInvariant() + Guid.NewGuid().ToString("N")[..6], null, null, new HashedPassword("h"), tenant.Id);
            if (wcfOperation) user.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenant.Id, user.Id, CollectorOperationCodes.Wcf, "head"));
            ctx.Add(user);
            foreach (var facility in facilities) ctx.Add(CollectorFacilityAssignment.Create(user.Id, facility.Id, facility.Code));
            return user;
        }
        var opOnly = Collector("OpOnly", true);
        var opTcc = Collector("OpTcc", true, tcc);
        var opNpm = Collector("OpNpm", true, npm);
        var opBoth = Collector("OpBoth", true, npm, tcc);
        var noOp = Collector("NoOp", false, npm, tcc);
        await ctx.SaveChangesAsync();
        return new(tenant.Id, Guid.NewGuid(), npmStall.Id, tccStall.Id, opOnly.Id, opTcc.Id, opNpm.Id, opBoth.Id, noOp.Id);
    }

    private static WcfCollectionWorkflow Wcf(AppDbContext ctx, World w, Guid userId, string role = "Collector") =>
        new(ctx, new Actor(userId, w.TenantId, role), new FixedTenant(w.TenantId), clock: new Clock());

    private static async Task EnableAsync(AppDbContext ctx, World w) =>
        Assert.True((await new WcfMobileCollectionWorkflow(ctx, new Actor(w.Head, w.TenantId, "Admin"), new FixedTenant(w.TenantId), new Clock()).EnableAsync()).IsSuccess);

    private static WcfCollectionPostRequest Direct(Guid stall, decimal amount) =>
        new(1, Guid.NewGuid(), Today, Guid.Empty, amount, 0, StallId: stall, BillingYear: Today.Year, BillingMonth: Today.Month);

    private static async Task<IReadOnlyList<Guid>> SeenAsync(AppDbContext ctx, World w, Guid collector) =>
        (await Wcf(ctx, w, collector).GetMobileSourcesAsync(Today.Year, Today.Month)).Value!.Select(s => s.StallId).ToList();

    [SkippableFact]
    public async Task OperationOnly_KeepsTheNpmContext_AndNothingElse()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        await EnableAsync(ctx, w);

        Assert.Equal(new[] { w.NpmStall }, await SeenAsync(ctx, w, w.OpOnly));
        Assert.False((await Wcf(ctx, w, w.OpOnly).PostMobileAsync(Direct(w.TccStall, 10m))).IsSuccess);
        Assert.True((await Wcf(ctx, w, w.OpOnly).PostMobileAsync(Direct(w.NpmStall, 10m))).IsSuccess);
    }

    [SkippableFact]
    public async Task OperationPlusTccOnly_NeverReachesNpm_InDiscoveryQuotesOrPosting()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        await EnableAsync(ctx, w);
        // An NPM obligation exists (collected by an NPM collector), so there is something to wrongly see or quote.
        Assert.True((await Wcf(ctx, w, w.OpNpm).PostMobileAsync(Direct(w.NpmStall, 25m))).IsSuccess);

        Assert.Equal(new[] { w.TccStall }, await SeenAsync(ctx, w, w.OpTcc));
        var quotes = (await Wcf(ctx, w, w.OpTcc).GetObligationsAsync(Today.Year, Today.Month)).Value!;
        Assert.DoesNotContain(quotes, q => q.StallId == w.NpmStall);
        var before = await ctx.Collections.CountAsync();
        Assert.False((await Wcf(ctx, w, w.OpTcc).PostMobileAsync(Direct(w.NpmStall, 10m))).IsSuccess);
        Assert.Equal(before, await ctx.Collections.CountAsync());
        Assert.True((await Wcf(ctx, w, w.OpTcc).PostMobileAsync(Direct(w.TccStall, 10m))).IsSuccess);
    }

    [SkippableFact]
    public async Task OperationPlusNpmOnly_NeverReachesTcc()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        await EnableAsync(ctx, w);

        Assert.Equal(new[] { w.NpmStall }, await SeenAsync(ctx, w, w.OpNpm));
        Assert.False((await Wcf(ctx, w, w.OpNpm).PostMobileAsync(Direct(w.TccStall, 10m))).IsSuccess);
        Assert.Equal(0, await ctx.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task MultipleExplicitAssignments_AreTheirUnion_AndNoOperationAssignmentIsForbidden()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        await EnableAsync(ctx, w);

        Assert.Equal(new[] { w.NpmStall, w.TccStall }.Order(), (await SeenAsync(ctx, w, w.OpBoth)).Order());
        Assert.True((await Wcf(ctx, w, w.OpBoth).PostMobileAsync(Direct(w.TccStall, 10m))).IsSuccess);
        Assert.True((await Wcf(ctx, w, w.OpBoth).PostMobileAsync(Direct(w.NpmStall, 10m))).IsSuccess);

        // Facility assignments without the WCF operation assignment are no authority for WCF at all.
        Assert.Equal(ResultStatus.Forbidden, (await Wcf(ctx, w, w.NoOp).GetMobileSourcesAsync(Today.Year, Today.Month)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Wcf(ctx, w, w.NoOp).GetObligationsAsync(Today.Year, Today.Month)).Status);
        var before = await ctx.Collections.CountAsync();
        Assert.False((await Wcf(ctx, w, w.NoOp).PostMobileAsync(Direct(w.TccStall, 10m))).IsSuccess);
        Assert.Equal(before, await ctx.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task ACrossTenantSource_IsNotFound_AndAForgedPostNamingAnOutOfScopeBillIsRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = await SeedAsync();                                   // a second tenant with its own stalls and collectors
        await using var ctx = db.CreateContext(w.TenantId);
        await EnableAsync(ctx, w);

        // Another tenant's stall ids are not visible to this tenant's collector.
        Assert.DoesNotContain(other.TccStall, await SeenAsync(ctx, w, w.OpBoth));
        Assert.False((await Wcf(ctx, w, w.OpBoth).PostMobileAsync(Direct(other.TccStall, 10m))).IsSuccess);

        // A forged post: the NPM bill's id, posted by the TCC-only collector, is refused and posts nothing.
        Assert.True((await Wcf(ctx, w, w.OpNpm).PostMobileAsync(Direct(w.NpmStall, 25m))).IsSuccess);
        var npmBill = await ctx.UtilityBills.SingleAsync(b => b.StallId == w.NpmStall);
        var before = await ctx.Collections.CountAsync();
        await Wcf(ctx, w, w.OpTcc).PostMobileAsync(
            new(1, Guid.NewGuid(), Today, npmBill.Id, 10m, npmBill.WaterSourceVersion, BillingYear: Today.Year, BillingMonth: Today.Month));
        Assert.Equal(before, await ctx.Collections.CountAsync());
    }
}
