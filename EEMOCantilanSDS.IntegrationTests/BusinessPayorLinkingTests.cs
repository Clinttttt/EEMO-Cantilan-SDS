using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// An occupancy that exists only as occupant text has no Business Payor, so nothing collected against it can be found by Payor. The
/// office links one explicitly. A name is never identity: nothing here links, merges or creates a Payor because two names match, an
/// occupancy already linked is never silently re-pointed, and the occupant text and history stay as recorded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BusinessPayorLinkingTests(PostgresFixture db)
{
    private sealed record World(Guid TenantId, Guid HeadId, Guid ContractId, Guid StallId, Guid BillId);

    private sealed class Actor(Guid userId, Guid tenantId, string role = "Admin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "payor-link";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static BusinessPayorWorkflow Linking(AppDbContext ctx, World w, string role = "Admin") =>
        new(ctx, new Actor(w.HeadId, w.TenantId, role), new FixedTenant(w.TenantId));

    private static CollectionComposerWorkflow Composer(AppDbContext ctx, World w) =>
        new(ctx, new Actor(w.HeadId, w.TenantId), new FixedTenant(w.TenantId));

    /// <summary>An NPM stall let to "Lisa Ilogans" as occupant text only (no Payor), with an assessed ECF bill and the ECF policy.</summary>
    private async Task<World> SeedAsync()
    {
        var municipality = Municipality.Create($"PL-{Guid.NewGuid():N}"[..20], "Payor Link", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"pl-{Guid.NewGuid():N}"[..30]);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }
        var tenant = municipality.Id;
        var period = PhilippineTime.Today;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenant);
        var stall = Stall.Create(facility.Id, "12", 0m, ApplicableFees.Electricity | ApplicableFees.Water,
            MarketSection.FishSection, createdBy: "test", municipalityId: tenant);
        var contract = Contract.Create(stall.Id, "Lisa Ilogans", "Lisa Ilogans",
            new DateOnly(period.Year, period.Month, 1).AddYears(-1), 5, 0m, createdBy: "test");
        var bill = UtilityBill.Create(stall.Id, period.Year, period.Month, 0m, 100m, 12m, 0m, 4m, 5m, "test");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Electricity Consumption Fee", RevenueInstrumentType.OfficialReceipt, tenant);
        await using (var ctx = db.CreateContext(tenant))
        {
            ctx.AddRange(facility, stall, contract, bill, classification, policy);
            await ctx.SaveChangesAsync();
        }
        return new World(tenant, Guid.NewGuid(), contract.Id, stall.Id, bill.Id);
    }

    [SkippableFact]
    public async Task AnOccupancyWithNoPayor_IsListedAsNeedingOne_AndAMatchingPayorNameLinksNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        // A Business Payor that happens to carry the same name exists — and the occupancy is still not linked to it.
        ctx.Payors.Add(Payor.Create(w.TenantId, "Lisa Ilogans", BusinessPayorKind.Person, "test"));
        await ctx.SaveChangesAsync();

        var needs = (await Linking(ctx, w).GetOccupanciesAsync(null, PayorLinkFilter.NeedsPayor)).Value!;
        var occupancy = Assert.Single(needs);
        Assert.Equal(("NPM", "12", "Lisa Ilogans", null), (occupancy.FacilityShortName, occupancy.StallNo, occupancy.ActualOccupant, occupancy.PayorId));
        Assert.Null((await ctx.Contracts.AsNoTracking().SingleAsync()).PayorId);
        Assert.Empty((await Linking(ctx, w).GetOccupanciesAsync(null, PayorLinkFilter.Linked)).Value!);

        // The existing Payor is offered as a candidate only; nothing is applied.
        var candidate = Assert.Single((await Linking(ctx, w).SearchPayorsAsync("lisa")).Value!);
        Assert.Empty(candidate.Contexts);
        Assert.Null((await ctx.Contracts.AsNoTracking().SingleAsync()).PayorId);
    }

    [SkippableFact]
    public async Task TheOfficeLinksAnExistingPayorExplicitly_TheOccupantTextStays_AndItIsNeverSilentlyRepointed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var payor = Payor.Create(w.TenantId, "Lisa Ilogans", BusinessPayorKind.Person, "test");
        var other = Payor.Create(w.TenantId, "Lisa Ilogan-Reyes", BusinessPayorKind.Person, "test");
        ctx.Payors.AddRange(payor, other);
        await ctx.SaveChangesAsync();
        var linking = Linking(ctx, w);

        var linked = await linking.LinkAsync(new(w.ContractId, payor.Id));
        Assert.True(linked.IsSuccess, linked.Error);
        Assert.False(linked.Value!.CreatedPayor);
        var contract = await ctx.Contracts.AsNoTracking().SingleAsync();
        Assert.Equal(payor.Id, contract.PayorId);
        Assert.Equal(("Lisa Ilogans", "Lisa Ilogans"), (contract.ActualOccupant, contract.NameOnContract));      // history is untouched

        // The same link again is a no-op; a different Payor is refused and nothing changes.
        Assert.True((await linking.LinkAsync(new(w.ContractId, payor.Id))).IsSuccess);
        var repoint = await linking.LinkAsync(new(w.ContractId, other.Id));
        Assert.False(repoint.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, repoint.Status);
        Assert.Equal(payor.Id, (await ctx.Contracts.AsNoTracking().SingleAsync()).PayorId);

        // Linked occupancies show their Payor and no longer need one; the candidate shows where it is used.
        Assert.Empty((await linking.GetOccupanciesAsync(null, PayorLinkFilter.NeedsPayor)).Value!);
        Assert.Equal("Lisa Ilogans", Assert.Single((await linking.GetOccupanciesAsync("ilogans", PayorLinkFilter.Linked)).Value!).PayorName);
        Assert.Equal("NPM · 12", Assert.Single(Assert.Single((await linking.SearchPayorsAsync("lisa ilogans")).Value!).Contexts));
    }

    [SkippableFact]
    public async Task CreatingAPayorNeverReusesASameNamedOne_UnlessTheOfficeConfirmsADifferentPerson()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        ctx.Payors.Add(Payor.Create(w.TenantId, "Lisa Ilogans", BusinessPayorKind.Person, "test"));
        await ctx.SaveChangesAsync();
        var linking = Linking(ctx, w);

        var refused = await linking.CreateAndLinkAsync(new(w.ContractId, " lisa ilogans ", BusinessPayorKind.Person));
        Assert.False(refused.IsSuccess);
        Assert.StartsWith("DUPLICATE_PAYOR", refused.Error);
        Assert.Equal(1, await ctx.Payors.CountAsync());                                    // nothing was written
        Assert.Null((await ctx.Contracts.AsNoTracking().SingleAsync()).PayorId);

        var confirmed = await linking.CreateAndLinkAsync(new(w.ContractId, "Lisa Ilogans", BusinessPayorKind.Person, ConfirmDuplicate: true));
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.True(confirmed.Value!.CreatedPayor);
        Assert.Equal(2, await ctx.Payors.CountAsync());
        Assert.Equal(confirmed.Value.PayorId, (await ctx.Contracts.AsNoTracking().SingleAsync()).PayorId);

        // Already linked: creating another is refused.
        Assert.Equal(ResultStatus.Conflict, (await linking.CreateAndLinkAsync(new(w.ContractId, "Someone Else", BusinessPayorKind.Person))).Status);
        Assert.False((await linking.CreateAndLinkAsync(new(w.ContractId, "  ", BusinessPayorKind.Person))).IsSuccess);
    }

    [SkippableFact]
    public async Task OnceLinked_CurrentCollectionFindsThePayor_AndItsEcfCharge()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var composer = Composer(ctx, w);

        // Before: the occupant is not a Payor, so the payer search cannot find her.
        Assert.Empty((await composer.SearchCollectionPayorsAsync("lisa")).Value!);

        var created = await Linking(ctx, w).CreateAndLinkAsync(new(w.ContractId, "Lisa Ilogans", BusinessPayorKind.Person));
        Assert.True(created.IsSuccess, created.Error);

        var found = Assert.Single((await composer.SearchCollectionPayorsAsync("lisa")).Value!);
        Assert.Equal(created.Value!.PayorId, found.PayorId);
        var charges = (await composer.GetPayorObligationsAsync(found.PayorId)).Value!;
        var ecf = Assert.Single(charges, x => x.SourceKind == CollectionSourceKind.UtilityBill);
        Assert.Equal(1200m, ecf.OutstandingAmount);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, ecf.Instrument);
    }

    [SkippableFact]
    public async Task OnlyOfficeStaffLink_AndAnotherTenantsOccupancyIsInvisible()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedAsync();
        var b = await SeedAsync();
        await using var ctxA = db.CreateContext(a.TenantId);
        await using var ctxB = db.CreateContext(b.TenantId);
        var payorB = Payor.Create(b.TenantId, "Lisa Ilogans", BusinessPayorKind.Person, "test");
        ctxB.Payors.Add(payorB);
        await ctxB.SaveChangesAsync();

        Assert.Equal(ResultStatus.Forbidden, (await Linking(ctxA, a, role: "Collector").LinkAsync(new(a.ContractId, payorB.Id))).Status);
        // Tenant B's Payor cannot be linked to tenant A's occupancy, and A cannot see B's occupancy.
        Assert.Equal(ResultStatus.NotFound, (await Linking(ctxA, a).LinkAsync(new(a.ContractId, payorB.Id))).Status);
        Assert.Equal(ResultStatus.NotFound, (await Linking(ctxA, a).LinkAsync(new(b.ContractId, payorB.Id))).Status);
        Assert.All((await Linking(ctxA, a).GetOccupanciesAsync(null, PayorLinkFilter.All)).Value!, o => Assert.Equal(a.StallId, o.StallId));
    }
}
