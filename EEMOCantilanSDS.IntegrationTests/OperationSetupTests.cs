using EEMOCantilanSDS.Application.Command.Settings.AddTenantOperation;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Settings.GetOperationSetup;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Revenue Sources "Available to add": adding an operation lists it and prepares its revenue source, and authorizes nothing. Through the real
/// handlers, tenant filters and PostgreSQL: omitted operations, an existing or retired source, a repeated or stale submission, tenant isolation and the
/// verified Available / Added / Setup required / Ready state.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationSetupTests(PostgresFixture db)
{
    private sealed class Head(Guid tenantId, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "ops-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static readonly DateOnly Today = PhilippineTime.Today;

    /// <summary>An onboarded municipality: a row for every catalog operation, only <paramref name="listed"/> selected.</summary>
    private async Task<Guid> SeedTenantAsync(params string[] listed)
    {
        var municipality = Municipality.Create($"OP-{Guid.NewGuid():N}"[..24], "Ops Test", "Province", MunicipalityStatus.Active, tenantCode: $"op-{Guid.NewGuid():N}"[..32]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(municipality.Id);
        ctx.TenantOperationSettings.AddRange(TenantOperationCatalog.All.Select(e =>
            TenantOperationSetting.Create(e.Code, listed.Contains(e.Code), null, "Onboarding", municipality.Id, "test")));
        await ctx.SaveChangesAsync();
        return municipality.Id;
    }

    private static AddTenantOperationCommandHandler AddHandler(AppDbContext ctx, Guid tenant, string role = "SuperAdmin") => new(ctx, new Head(tenant, role));
    private static GetOperationSetupQueryHandler SetupHandler(AppDbContext ctx, Guid tenant) => new(ctx, new Head(tenant), new FixedTenant(tenant), new SystemClock());

    private static async Task<OperationSetupDto> StateOf(AppDbContext ctx, Guid tenant, string code) =>
        (await SetupHandler(ctx, tenant).Handle(new GetOperationSetupQuery(), default)).Value!.Items.Single(i => i.OperationCode == code);

    [SkippableFact]
    public async Task AddingAnOmittedOperation_ListsIt_CreatesItsSource_AndAuthorizesNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(CollectorOperationCodes.MarketFees);
        await using var ctx = db.CreateContext(tenant);
        Assert.Equal(OperationSetupState.Available, (await StateOf(ctx, tenant, "ECF")).State);

        var added = await AddHandler(ctx, tenant).Handle(new("ECF", "Electric Fees"), default);

        Assert.True(added.IsSuccess, added.Error);
        Assert.Equal((false, true, false), (added.Value!.AlreadyAdded, added.Value.SourceCreated, added.Value.SourceRetired));
        var item = added.Value.Settings.Items.Single(i => i.OperationCode == "ECF");
        Assert.Equal((true, "Electric Fees"), (item.IsListed, item.LocalDisplayName));

        await using var verify = db.CreateContext(tenant);
        Assert.Equal(1, await verify.RevenueClassifications.CountAsync(c => c.SemanticCode == RevenueClassificationCodes.Ecf));
        Assert.Empty(await verify.RevenueClassificationPolicies.ToListAsync());                 // no policy: no receipt type chosen for the office
        Assert.Empty(await verify.CollectorOperationAssignments.ToListAsync());                 // no collector assigned
        Assert.Empty(await verify.CollectorOperationActivations.ToListAsync());                 // nothing activated
        Assert.Empty(await verify.Collections.ToListAsync());
        var state = await StateOf(verify, tenant, "ECF");
        Assert.Equal((OperationSetupState.SetupRequired, true, false, false), (state.State, state.SourceExists, state.SourceRetired, state.PolicyEffective));
        Assert.Contains("No effective policy (receipt type) yet", state.Gaps);
        Assert.Equal(OperationSetupState.Available, (await StateOf(verify, tenant, "WCF")).State);   // an operation nobody added stays available
    }

    [SkippableFact]
    public async Task AnExistingSourceIsReused_AndARetiredOneIsNeverReactivated()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync();
        await using (var seed = db.CreateContext(tenant))
        {
            seed.RevenueClassifications.Add(RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, tenant, "test"));
            var retired = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenant, "test");
            retired.Retire("test");
            seed.RevenueClassifications.Add(retired);
            await seed.SaveChangesAsync();
        }
        await using var ctx = db.CreateContext(tenant);

        var reused = (await AddHandler(ctx, tenant).Handle(new(CollectorOperationCodes.Transportation, null), default)).Value!;
        var kept = (await AddHandler(ctx, tenant).Handle(new(CollectorOperationCodes.Wcf, null), default)).Value!;

        Assert.False(reused.SourceCreated);                                                       // reused as it is, never duplicated
        Assert.True(kept.SourceRetired); Assert.False(kept.SourceCreated);
        await using var verify = db.CreateContext(tenant);
        Assert.Equal(1, await verify.RevenueClassifications.CountAsync(c => c.SemanticCode == RevenueClassificationCodes.TransportationParking));
        var wcf = await verify.RevenueClassifications.SingleAsync(c => c.SemanticCode == RevenueClassificationCodes.Wcf);
        Assert.False(wcf.IsActive);                                                               // not silently reactivated
        var state = await StateOf(verify, tenant, CollectorOperationCodes.Wcf);
        Assert.Equal(OperationSetupState.SetupRequired, state.State);
        Assert.Contains("Its revenue source is retired", state.Gaps);
    }

    [SkippableFact]
    public async Task ARepeatedAdd_ChangesNothing_AStaleVersionIsAConflict_AndABadNameIsRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync();
        await using var ctx = db.CreateContext(tenant);
        var version = (await new EEMOCantilanSDS.Application.Queries.Settings.GetTenantOperationSettings.GetTenantOperationSettingsQueryHandler(ctx, new Head(tenant))
            .Handle(new(), default)).Value!.Items.Single(i => i.OperationCode == "ECF").Version;

        var stale = await AddHandler(ctx, tenant).Handle(new("ECF", null, version + 5), default);
        Assert.False(stale.IsSuccess); Assert.Equal(ResultStatus.Conflict, stale.Status);

        var taken = await AddHandler(ctx, tenant).Handle(new("ECF", TenantOperationCatalog.Find(CollectorOperationCodes.Wcf)!.DefaultLabel), default);
        Assert.False(taken.IsSuccess); Assert.Equal(ResultStatus.Invalid, taken.Status);          // reads as another operation's standard name
        await using (var untouched = db.CreateContext(tenant))
        {
            Assert.False((await untouched.TenantOperationSettings.SingleAsync(s => s.OperationCode == "ECF")).IsListed);
            Assert.Empty(await untouched.RevenueClassifications.ToListAsync());
        }

        var first = await AddHandler(ctx, tenant).Handle(new("ECF", null, version), default);
        Assert.True(first.IsSuccess, first.Error);
        await using var again = db.CreateContext(tenant);
        var second = await AddHandler(again, tenant).Handle(new("ECF", null, version), default);
        Assert.True(second.IsSuccess); Assert.True(second.Value!.AlreadyAdded);
        await using var verify = db.CreateContext(tenant);
        Assert.Equal(1, await verify.RevenueClassifications.CountAsync());
        Assert.Equal(version + 1, (await verify.TenantOperationSettings.SingleAsync(s => s.OperationCode == "ECF")).Version);   // listed once, not edited twice
    }

    [SkippableFact]
    public async Task OnlyTheHeadAdds_AMunicipalityCannotReachAnothers_AndAnUnadoptedOneHasNothingToAdd()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync();
        var other = await SeedTenantAsync();
        var legacy = Municipality.Create($"LG-{Guid.NewGuid():N}"[..24], "Legacy", "Province", MunicipalityStatus.Active, tenantCode: $"lg-{Guid.NewGuid():N}"[..32]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(legacy); await setup.SaveChangesAsync(); }

        await using (var ctx = db.CreateContext(tenant))
            Assert.Equal(ResultStatus.Forbidden, (await AddHandler(ctx, tenant, "Admin").Handle(new("ECF", null), default)).Status);

        await using (var ctx = db.CreateContext(tenant))
            Assert.True((await AddHandler(ctx, tenant).Handle(new("ECF", null), default)).IsSuccess);
        await using (var ctx = db.CreateContext(other))
        {
            Assert.False((await ctx.TenantOperationSettings.SingleAsync(s => s.OperationCode == "ECF")).IsListed);   // the other municipality is untouched
            Assert.Empty(await ctx.RevenueClassifications.ToListAsync());
            Assert.Equal(OperationSetupState.Available, (await StateOf(ctx, other, "ECF")).State);
        }

        await using var legacyCtx = db.CreateContext(legacy.Id);
        var none = await AddHandler(legacyCtx, legacy.Id).Handle(new("ECF", null), default);
        Assert.True(none.IsSuccess); Assert.True(none.Value!.AlreadyAdded);                         // every operation is already listed for it
        await using var verify = db.CreateContext(legacy.Id);
        Assert.Empty(await verify.TenantOperationSettings.ToListAsync());                          // adding adopted nothing and wrote nothing
        Assert.Empty(await verify.RevenueClassifications.ToListAsync());
    }

    [SkippableFact]
    public async Task TheStateIsAddedWithoutACollector_SetupRequiredWithAnInactiveOne_AndReadyOnlyWhenACollectorCanCollect()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var code = CollectorOperationCodes.KanmanggaySpaceRental;
        var tenant = await SeedTenantAsync(code);
        Guid collectorId;
        await using (var seed = db.CreateContext(tenant))
        {
            var source = RevenueClassification.Create(RevenueClassificationCodes.KanmanggaySpaceRental, tenant, "test");
            seed.RevenueClassifications.Add(source);
            seed.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(source.Id, Today.AddYears(-1), "Space rental", RevenueInstrumentType.OfficialReceipt, tenant));
            var collector = CollectorUser.Create("Ops Collector", "OC-01", "oc-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("test-hash"), tenant);
            collectorId = collector.Id;
            seed.CollectorUsers.Add(collector);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext(tenant))
        {
            var added = await StateOf(ctx, tenant, code);
            Assert.Equal(OperationSetupState.Added, added.State);                                   // a source and an effective policy, nobody asked to collect it
            Assert.Contains("No collector assigned yet", added.Gaps);
        }

        await using (var assign = db.CreateContext(tenant))
        {
            assign.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(tenant, collectorId, code, "head"));
            await assign.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            var ready = await StateOf(ctx, tenant, code);
            Assert.Equal((OperationSetupState.Ready, 1, 1), (ready.State, ready.AssignedCollectors, ready.ReadyCollectors));
            Assert.Empty(ready.Gaps);
        }

        await using (var off = db.CreateContext(tenant))
        {
            (await off.CollectorUsers.SingleAsync(c => c.Id == collectorId)).Deactivate("head");
            await off.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            var blocked = await StateOf(ctx, tenant, code);
            Assert.Equal(OperationSetupState.SetupRequired, blocked.State);
            Assert.Contains("An assigned collector is inactive", blocked.Gaps);
        }
    }
}
