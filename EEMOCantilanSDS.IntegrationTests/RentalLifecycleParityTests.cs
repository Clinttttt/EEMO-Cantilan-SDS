using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using static EEMOCantilanSDS.IntegrationTests.SpaceTestKit;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// One rental lifecycle across sources, on real PostgreSQL: a stall at TCC, NCC, BBQ or Ice Plant and a monthly space at Kanmanggay or Vegetable/Fruit
/// are, for the office, rental accounts. In the same state they must offer the same Resume / Renew actions to the same roles, and a space's Reassign and
/// Remove follow the same authority as a stall's. (A stall's own Assign-new-stall and its commands are the existing, separately tested ones.)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RentalLifecycleParityTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static StallLifecycle StallAdapter(AppDbContext context) => new(new StallRepository(context), context, new SystemClock());
    private static SpaceAccountLifecycle SpaceAdapter(AppDbContext context, Guid tenant) => new(context, new FixedTenant(tenant), Workflow(context, tenant));

    private static bool Can(AccountLifecycleDto view, LifecycleAction action) => view.Actions.Single(a => a.Action == action).Allowed;

    private sealed record Stalls(Guid Tenant, Guid Closed, Guid Lapsed, Guid Running);

    private async Task<Stalls> SeedStallsAsync(FacilityCode code)
    {
        var tenant = await SeedTenantAsync(db, $"Stall {code}");
        var facility = Facility.Create(code, code.ToString(), code.ToString(), municipalityId: tenant);
        Stall NewStall(string no) => Stall.Create(facility.Id, no, 900m, ApplicableFees.BaseRental, createdBy: "test", municipalityId: tenant);
        var closed = NewStall("1"); closed.Close(new DateOnly(2026, 8, 30), "test");
        var lapsed = NewStall("2");
        var running = NewStall("3");
        Contract Term(Stall s, DateOnly from, int years) => Contract.Create(s.Id, "Lessee", "Lessee", from, years, 900m, createdBy: "test");
        await using var context = db.CreateContext(tenant);
        context.Add(facility);
        context.AddRange(closed, lapsed, running, Term(closed, new DateOnly(2025, 1, 1), 5), Term(lapsed, new DateOnly(2020, 1, 1), 1), Term(running, new DateOnly(2025, 1, 1), 5));
        await context.SaveChangesAsync();
        return new(tenant, closed.Id, lapsed.Id, running.Id);
    }

    [SkippableTheory]
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.NPM)]
    [InlineData(FacilityCode.NCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    public async Task EachStallFacility_OffersTheRentalActionsItsStateImplies_ToTheSameRoles(FacilityCode code)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var s = await SeedStallsAsync(code);
        await using var context = db.CreateContext(s.Tenant);
        var adapter = StallAdapter(context);

        var closed = (await adapter.GetAsync(LifecycleSource.Stall, s.Closed, "Admin", default))!;
        Assert.Equal(LifecycleState.Closed, closed.State);
        Assert.True(Can(closed, LifecycleAction.Resume));
        Assert.False(Can(closed, LifecycleAction.Renew));
        Assert.False(Can(closed, LifecycleAction.Remove));                                                      // Admin: Head only
        Assert.True(Can((await adapter.GetAsync(LifecycleSource.Stall, s.Closed, "SuperAdmin", default))!, LifecycleAction.Remove));

        var lapsed = (await adapter.GetAsync(LifecycleSource.Stall, s.Lapsed, "Admin", default))!;
        Assert.True(Can(lapsed, LifecycleAction.Renew));
        Assert.False(Can(lapsed, LifecycleAction.Resume));

        var running = (await adapter.GetAsync(LifecycleSource.Stall, s.Running, "Admin", default))!;
        Assert.False(Can(running, LifecycleAction.Resume) || Can(running, LifecycleAction.Renew));
    }

    [SkippableTheory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    public async Task EachSpaceSource_OffersTheSameRentalActionsInTheSameStates_ToTheSameRoles(ObligationKind kind)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, $"Space {kind}");
        var closed = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), kind: kind);
        var lapsed = await AddAsync(db, tenant, "2", new DateOnly(2020, 1, 1), kind: kind, signed: true);       // a signed term that ran out in 2022
        var running = await AddAsync(db, tenant, "3", new DateOnly(2026, 1, 1), kind: kind, signed: true);
        var spaceOnly = await AddAsync(db, tenant, "4", new DateOnly(2020, 1, 1), kind: kind);                  // no contract: nothing to renew
        await using var context = db.CreateContext(tenant);
        var adapter = SpaceAdapter(context, tenant);
        async Task<AccountLifecycleDto> Read(Guid id, string role) => (await adapter.GetAsync(LifecycleSource.SpaceRental, id, role, default))!;

        var closedAdmin = await Read(closed.Account.Id, "Admin");
        Assert.Equal(LifecycleState.Closed, closedAdmin.State);
        Assert.True(Can(closedAdmin, LifecycleAction.Resume));
        Assert.True(Can(closedAdmin, LifecycleAction.Reassign));
        Assert.False(Can(closedAdmin, LifecycleAction.Renew));
        Assert.False(Can(closedAdmin, LifecycleAction.Remove));                                                 // Admin: Head only, as for a stall
        Assert.True(Can(await Read(closed.Account.Id, "SuperAdmin"), LifecycleAction.Remove));

        var lapsedView = await Read(lapsed.Account.Id, "Admin");
        Assert.True(Can(lapsedView, LifecycleAction.Renew));
        Assert.False(Can(lapsedView, LifecycleAction.Resume));
        var runningView = await Read(running.Account.Id, "Admin");
        Assert.False(Can(runningView, LifecycleAction.Resume) || Can(runningView, LifecycleAction.Renew));
        Assert.False(Can(await Read(spaceOnly.Account.Id, "Admin"), LifecycleAction.Renew));                    // SpaceOnly: no recorded contract term to renew
        Assert.False(Can(await Read(closed.Account.Id, "Collector"), LifecycleAction.Resume));
        Assert.Equal(("1", "Ana Reyes"), (closedAdmin.Label, closedAdmin.Holder));                                // the label and holder are the account's own
    }

    [SkippableTheory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    public async Task EachSpaceSource_ResumesReassignsRenewsAndRetires_WithoutMovingMoneyOrOverlapping(ObligationKind kind)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, $"Flow {kind}");
        var toResume = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), kind: kind);
        var toReassign = await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), kind: kind);
        var toRenew = await AddAsync(db, tenant, "3", new DateOnly(2021, 1, 1), kind: kind, signed: true);
        var toRetire = await AddAsync(db, tenant, "4", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), kind: kind, rateFrom: new DateOnly(2026, 11, 1));
        await AssessAsync(db, tenant, toResume.Account, toResume.Rate, new DateOnly(2026, 2, 1));          // the previous holder's own debt
        var jul1 = new DateOnly(2026, 7, 1);

        async Task<EEMOCantilanSDS.Application.Common.Result<LifecycleOutcome>> Run(Guid id, LifecycleCommand command, string role = "SuperAdmin")
        {
            await using var context = db.CreateContext(tenant);
            var workflow = Workflow(context, tenant, role);
            var preview = await workflow.PreviewAsync(id, command);
            return await workflow.ExecuteAsync(id, new(Guid.NewGuid(), preview.Value!.Version, command));
        }

        var resumed = await Run(toResume.Account.Id, new(LifecycleAction.Resume, jul1, "Same holder returns"), "Admin");
        var reassigned = await Run(toReassign.Account.Id, new(LifecycleAction.Reassign, jul1, "New holder", NewHolder: "Pedro Cruz", Amount: 1500m), "Admin");
        var renewed = await Run(toRenew.Account.Id, new(LifecycleAction.Renew, new DateOnly(2024, 1, 1), "Renewal", TermYears: 3), "Admin");
        var retired = await Run(toRetire.Account.Id, new(LifecycleAction.Remove, null, "Space removed from the roll"));
        Assert.True(resumed.IsSuccess && reassigned.IsSuccess && renewed.IsSuccess && retired.IsSuccess, string.Join(" | ", new[] { resumed, reassigned, renewed, retired }.Select(r => r.Error)));

        await using var verify = db.CreateContext(tenant);
        var accounts = verify.ObligationAccounts.AsNoTracking().ToList();
        Assert.Equal(6, accounts.Count);                                                                     // two successors, no other account created
        var resumedSuccessor = accounts.Single(a => a.Id == resumed.Value!.ResultAccountId);
        Assert.Equal((toResume.Registration.Id, jul1, true), (resumedSuccessor.SpaceRegistrationId!.Value, resumedSuccessor.ActiveFrom, resumedSuccessor.ActiveTo is null));
        Assert.Equal("Pedro Cruz", accounts.Single(a => a.Id == reassigned.Value!.ResultAccountId).ActualOccupant);
        Assert.Equal(1, verify.ObligationPeriods.Count());                                                   // no period for any successor, none for the closed gap: nothing billed or moved
        Assert.Equal(1, verify.SpaceContractTerms.Count());
        Assert.NotNull(accounts.Single(a => a.Id == toRetire.Account.Id).RetiredAtUtc);
        Assert.Equal(4, verify.SpaceLifecycleEvents.Count());
    }
}
