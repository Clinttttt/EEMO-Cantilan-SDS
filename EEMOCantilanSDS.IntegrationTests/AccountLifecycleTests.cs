using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The read-only lifecycle adapters against real PostgreSQL and the real tenant query filters: another municipality's stall or space
/// account is never visible, a closed stall reads as closed, and reading changes nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountLifecycleTests(PostgresFixture db)
{
    private sealed record Seeded(Guid Id, Guid StallId, Guid ClosedStallId, Guid SpaceId, Guid LotId);

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private async Task<Seeded> SeedAsync(string name)
    {
        var municipality = Municipality.Create($"LC-{Guid.NewGuid():N}"[..24], name, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: $"lc-{Guid.NewGuid():N}"[..32]);
        var tenant = municipality.Id;
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: tenant);
        var open = Stall.Create(facility.Id, "A-1", 900m, ApplicableFees.BaseRental, createdBy: "test", municipalityId: tenant);
        var closed = Stall.Create(facility.Id, "A-2", 900m, ApplicableFees.BaseRental, createdBy: "test", municipalityId: tenant);
        closed.Close(new DateOnly(2026, 8, 30), "test");
        var openTerm = Contract.Create(open.Id, "Open Lessee", "Open Lessee", new DateOnly(2025, 1, 1), 5, 900m, createdBy: "test");
        var closedTerm = Contract.Create(closed.Id, "Closed Lessee", "Closed Lessee", new DateOnly(2025, 1, 1), 5, 900m, createdBy: "test");
        var space = ObligationAccount.Create(tenant, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "12", null, null,
            new DateOnly(2026, 1, 1), "test", actualOccupant: "Space Holder");
        var lot = ObligationAccount.Create(tenant, ObligationKind.FiestaArawLotRental, Guid.Empty, null, "3", LotRentalEvent.Fiesta,
            new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15), "test", actualOccupant: "Lot Holder");

        await using var context = db.CreateContext(tenant);
        context.Add(facility); context.AddRange(open, closed, openTerm, closedTerm, space, lot);
        await context.SaveChangesAsync();
        return new(tenant, open.Id, closed.Id, space.Id, lot.Id);
    }

    private static StallLifecycle StallAdapter(AppDbContext context) => new(new StallRepository(context), context, new SystemClock());
    private static SpaceAccountLifecycle SpaceAdapter(AppDbContext context, Guid tenant) => new(context, new Tenant(tenant), SpaceTestKit.Workflow(context, tenant));

    [SkippableFact]
    public async Task Stalls_AreReadOnlyThroughTheTenantFilter_AndAClosedStallReadsAsClosed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedAsync("Tenant A");
        var b = await SeedAsync("Tenant B");

        await using var context = db.CreateContext(a.Id);
        var adapter = StallAdapter(context);

        var closed = await adapter.GetAsync(LifecycleSource.Stall, a.ClosedStallId, "SuperAdmin", default);
        Assert.Equal((LifecycleState.Closed, "A-2", "TCC", "Closed Lessee"), (closed!.State, closed.Label, closed.SourceDetail, closed.Holder));
        Assert.Equal(new DateOnly(2026, 8, 30), closed.EndedOn);
        Assert.True(closed.Actions.Single(x => x.Action == LifecycleAction.Resume).Allowed);
        Assert.False(closed.Actions.Single(x => x.Action == LifecycleAction.Close).Allowed);

        var open = await adapter.GetAsync(LifecycleSource.Stall, a.StallId, "Admin", default);
        Assert.Equal(LifecycleState.Active, open!.State);
        Assert.False(open.Actions.Single(x => x.Action == LifecycleAction.Remove).Allowed);

        // Another municipality's stalls are simply not found, by id, for every state.
        Assert.Null(await adapter.GetAsync(LifecycleSource.Stall, b.StallId, "SuperAdmin", default));
        Assert.Null(await adapter.GetAsync(LifecycleSource.Stall, b.ClosedStallId, "SuperAdmin", default));

        // Reading changed nothing: no tracked change, and the closed stall is still closed with its date.
        Assert.False(context.ChangeTracker.HasChanges());
        await using var verify = db.CreateContext(a.Id);
        var stall = await verify.Stalls.SingleAsync(s => s.Id == a.ClosedStallId);
        Assert.Equal((StallStatus.Closed, new DateOnly?(new DateOnly(2026, 8, 30))), (stall.Status, stall.ClosedAt));
    }

    [SkippableFact]
    public async Task SpaceAccounts_AreTenantScoped_KeepSourceKindsApart_AndOfferOnlyWhatTheWorkflowDoes()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedAsync("Tenant A");
        var b = await SeedAsync("Tenant B");

        await using var context = db.CreateContext(a.Id);
        var adapter = SpaceAdapter(context, a.Id);

        var space = await adapter.GetAsync(LifecycleSource.SpaceRental, a.SpaceId, "SuperAdmin", default);
        Assert.Equal((LifecycleSource.SpaceRental, "12", "Space Holder", LifecycleState.Active), (space!.Source, space.Label, space.Holder, space.State));
        Assert.True(space.Actions.Single(x => x.Action == LifecycleAction.Close).Allowed);
        Assert.All(new[] { LifecycleAction.Resume, LifecycleAction.Renew, LifecycleAction.Reassign, LifecycleAction.Remove },
            action => Assert.Equal(SpaceLifecycleWorkflow.NoSpaceRegistration, space.Actions.Single(x => x.Action == action).ReasonCode));

        // The same account id under another municipality, and the event lot under the wrong source, are not found.
        Assert.Null(await adapter.GetAsync(LifecycleSource.SpaceRental, b.SpaceId, "SuperAdmin", default));
        Assert.Null(await adapter.GetAsync(LifecycleSource.SpaceRental, a.LotId, "SuperAdmin", default));
        var lot = await adapter.GetAsync(LifecycleSource.EventLot, a.LotId, "SuperAdmin", default);
        Assert.All(new[] { LifecycleAction.Resume, LifecycleAction.Renew, LifecycleAction.Reassign, LifecycleAction.Remove },
            action => Assert.Equal(LifecycleReason.NotApplicableToEventLot, lot!.Actions.Single(x => x.Action == action).ReasonCode));

        // A foreign tenant's accessor reading tenant A's id finds nothing, even through a context scoped to tenant A.
        Assert.Null(await SpaceAdapter(context, b.Id).GetAsync(LifecycleSource.SpaceRental, a.SpaceId, "SuperAdmin", default));
        Assert.False(context.ChangeTracker.HasChanges());
    }
}
