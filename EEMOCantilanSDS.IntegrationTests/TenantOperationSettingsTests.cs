using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Settings.GetTenantOperationSettings;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A municipality's operation directory configuration against a real PostgreSQL: the legacy fallback for a municipality that
/// never adopted it, explicit adoption on a Head's first edit, tenant isolation, Head-only edits, optimistic concurrency, and
/// that configuration is presentation only - it files no rate, assigns no Collector and enables no source.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantOperationSettingsTests(PostgresFixture db)
{
    private sealed class User(Guid tenantId, string role, string name = "head") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => name;
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "x";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record Tenants(Guid Cantilan, Guid Carrascal);

    private async Task<Tenants> SeedAsync()
    {
        await db.ResetAsync();
        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
        var carrascal = Municipality.Create("CARRASCAL", "Carrascal", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "carrascal");
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.AddRange(cantilan, carrascal);
        await setup.SaveChangesAsync();
        return new Tenants(cantilan.Id, carrascal.Id);
    }

    private async Task<TenantOperationSettingsDto> ReadAsync(Guid tenant, string role = "SuperAdmin")
    {
        await using var ctx = db.CreateContext(tenant);
        var r = await new GetTenantOperationSettingsQueryHandler(ctx, new User(tenant, role))
            .Handle(new GetTenantOperationSettingsQuery(), CancellationToken.None);
        Assert.True(r.IsSuccess, r.Error);
        return r.Value!;
    }

    private async Task<Result<TenantOperationSettingsDto>> EditAsync(
        Guid tenant, string code, bool listed, string? label, long? version = null, string role = "SuperAdmin")
    {
        await using var ctx = db.CreateContext(tenant);
        return await new UpdateTenantOperationSettingCommandHandler(ctx, new User(tenant, role))
            .Handle(new UpdateTenantOperationSettingCommand(code, listed, label, version), CancellationToken.None);
    }

    [SkippableFact]
    public async Task AMunicipalityThatNeverAdopted_IsReadAsEverythingListedUnderItsEstablishedNames()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();

        var cantilan = await ReadAsync(t.Cantilan);

        Assert.False(cantilan.UsesConfiguration);
        Assert.Equal(TenantOperationCatalog.All.Count, cantilan.Items.Count);
        Assert.All(cantilan.Items, i => Assert.True(i.IsListed)); // "no rows" is NOT "nothing enabled"
        Assert.Equal("Kanmanggay", cantilan.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").DisplayName);
        Assert.Equal("Fiesta | Araw", cantilan.Items.Single(i => i.OperationCode == "FIESTA_ARAW_LOT_RENTAL").DisplayName);
        await using var verify = db.CreateContext(Guid.Empty);
        Assert.Equal(0, await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync()); // reading writes nothing
    }

    [SkippableFact]
    public async Task TheFirstHeadEdit_AdoptsExplicitly_WithoutRenamingOrHidingAnythingElse()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();

        var edit = await EditAsync(t.Cantilan, "LANDING_BERTHING", listed: false, label: null);

        Assert.True(edit.IsSuccess, edit.Error);
        var after = edit.Value!;
        Assert.True(after.UsesConfiguration);
        Assert.False(after.Items.Single(i => i.OperationCode == "LANDING_BERTHING").IsListed);
        // Everything else is still listed, under the same wording it had before.
        Assert.All(after.Items.Where(i => i.OperationCode != "LANDING_BERTHING"), i => Assert.True(i.IsListed));
        Assert.Equal("Kanmanggay", after.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").DisplayName);
        Assert.Equal("Market Fees", after.Items.Single(i => i.OperationCode == "MARKET_FEES").DisplayName);
        await using var verify = db.CreateContext(Guid.Empty);
        Assert.Equal(TenantOperationCatalog.All.Count,
            await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync(s => s.MunicipalityId == t.Cantilan));
        Assert.Equal(0, await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync(s => s.MunicipalityId == t.Carrascal));
    }

    [SkippableFact]
    public async Task OnlyTheHead_MayEdit_AndAnAdminOrAnotherTenantsHeadChangesNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();

        var admin = await EditAsync(t.Cantilan, "MARKET_FEES", false, null, role: "Admin");
        Assert.Equal(ResultStatus.Forbidden, admin.Status);
        var collector = await EditAsync(t.Cantilan, "MARKET_FEES", false, null, role: "Collector");
        Assert.Equal(ResultStatus.Forbidden, collector.Status);

        // Carrascal's Head edits Carrascal. Cantilan is untouched, because the municipality is the token's, never the request's.
        Assert.True((await EditAsync(t.Carrascal, "MARKET_FEES", false, "Pamilihan Fees")).IsSuccess);
        var cantilan = await ReadAsync(t.Cantilan);
        Assert.False(cantilan.UsesConfiguration);
        Assert.True(cantilan.Items.Single(i => i.OperationCode == "MARKET_FEES").IsListed);
        Assert.Equal("Market Fees", cantilan.Items.Single(i => i.OperationCode == "MARKET_FEES").DisplayName);

        var carrascal = await ReadAsync(t.Carrascal);
        Assert.False(carrascal.Items.Single(i => i.OperationCode == "MARKET_FEES").IsListed);
        await using var verify = db.CreateContext(Guid.Empty);
        Assert.Equal(0, await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync(s => s.MunicipalityId == t.Cantilan));
    }

    [SkippableFact]
    public async Task ALocalNameChangesOnlyTheLabel_NeverTheStableCode()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();

        var edit = await EditAsync(t.Carrascal, "KANMANGGAY_SPACE_RENTAL", true, "  Plaza Rental  ");

        var row = edit.Value!.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL");
        Assert.Equal("Plaza Rental", row.DisplayName);
        Assert.Equal("Municipal Space Rental", row.DefaultLabel); // the neutral name is kept beside it
        Assert.Equal(TenantOperationCatalog.SpaceGroup, row.GroupKey);

        // Restoring the neutral name clears the label; the code never moved.
        var restored = await EditAsync(t.Carrascal, "KANMANGGAY_SPACE_RENTAL", true, null);
        Assert.Equal("Municipal Space Rental", restored.Value!.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").DisplayName);
    }

    [SkippableFact]
    public async Task ANameThatImpersonatesAnotherOperationOrRepeatsAListedOne_IsRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();

        var impersonate = await EditAsync(t.Carrascal, "ECF", true, "Water Consumption Fees");
        Assert.Equal(ResultStatus.Invalid, impersonate.Status);

        Assert.True((await EditAsync(t.Carrascal, "KANMANGGAY_SPACE_RENTAL", true, "Plaza Rental")).IsSuccess);
        var repeat = await EditAsync(t.Carrascal, "FIESTA_ARAW_LOT_RENTAL", true, "plaza rental");
        Assert.Equal(ResultStatus.Invalid, repeat.Status);

        var unknown = await EditAsync(t.Carrascal, "Kanmanggay Space Rental", true, null);
        Assert.Equal(ResultStatus.Invalid, unknown.Status); // a display string is never accepted as an identity
    }

    [SkippableFact]
    public async Task AStaleEdit_IsRefusedAsAConflict_NotSilentlyOverwritten()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        var first = await EditAsync(t.Carrascal, "MARKET_FEES", true, "Fees A");
        var seen = first.Value!.Items.Single(i => i.OperationCode == "MARKET_FEES").Version;

        // Another Head saves from the same starting point.
        Assert.True((await EditAsync(t.Carrascal, "MARKET_FEES", true, "Fees B", version: seen)).IsSuccess);
        var stale = await EditAsync(t.Carrascal, "MARKET_FEES", true, "Fees C", version: seen);

        Assert.Equal(ResultStatus.Conflict, stale.Status);
        var now = await ReadAsync(t.Carrascal);
        Assert.Equal("Fees B", now.Items.Single(i => i.OperationCode == "MARKET_FEES").DisplayName);
    }

    [SkippableFact]
    public async Task TheMeedoThreeYearContractRule_IsFlaggedForEveryMunicipalityExceptTheDefaultOne_AndNothingElseIsFlagged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync(); // Cantilan (default) and Carrascal

        var cantilan = await ReadAsync(t.Cantilan);
        var carrascal = await ReadAsync(t.Carrascal);

        // Cantilan's own accepted policy is not a warning to Cantilan.
        Assert.All(cantilan.Items, i => Assert.Null(i.PolicyNotice));
        // Another municipality is told, on exactly the operation the rule is attached to.
        var flagged = carrascal.Items.Where(i => i.PolicyNotice is not null).Select(i => i.OperationCode).ToArray();
        Assert.Equal(new[] { "KANMANGGAY_SPACE_RENTAL" }, flagged);
        Assert.Contains("three-year", carrascal.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").PolicyNotice);
        // Flagging never changes what is listed or what an operation is called.
        Assert.All(carrascal.Items, i => Assert.True(i.IsListed));
    }

    [SkippableFact]
    public async Task TheDatabase_RefusesTwoSettingsForOneOperationInOneMunicipality()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        await using (var ctx = db.CreateContext(Guid.Empty))
        {
            ctx.TenantOperationSettings.Add(TenantOperationSetting.Create("ECF", true, null, "Head", t.Carrascal, "test"));
            await ctx.SaveChangesAsync();
        }

        await using var again = db.CreateContext(Guid.Empty);
        again.TenantOperationSettings.Add(TenantOperationSetting.Create("ECF", false, null, "Head", t.Carrascal, "test"));
        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());

        // The same code in ANOTHER municipality is a different row and is fine.
        await using var other = db.CreateContext(Guid.Empty);
        other.TenantOperationSettings.Add(TenantOperationSetting.Create("ECF", true, null, "Head", t.Cantilan, "test"));
        await other.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task EachOperationReportsHowManyCollectorsAreAssigned_AsAFactAndNothingMore()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        await using (var ctx = db.CreateContext(Guid.Empty))
        {
            var collector = CollectorUser.Create("Field Collector", "C-01", $"c{Guid.NewGuid():N}"[..12], null, null, new HashedPassword("h"), t.Carrascal);
            ctx.CollectorUsers.Add(collector);
            ctx.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(t.Carrascal, collector.Id, CollectorOperationCodes.FiestaArawLotRental, "test"));
            await ctx.SaveChangesAsync();
        }

        var settings = await ReadAsync(t.Carrascal);

        Assert.Equal(1, settings.Items.Single(i => i.OperationCode == CollectorOperationCodes.FiestaArawLotRental).AssignedCollectors);
        Assert.All(settings.Items.Where(i => i.OperationCode != CollectorOperationCodes.FiestaArawLotRental), i => Assert.Equal(0, i.AssignedCollectors));
        Assert.Equal(0, (await ReadAsync(t.Cantilan)).Items.Sum(i => i.AssignedCollectors)); // another municipality never sees it
    }
}
