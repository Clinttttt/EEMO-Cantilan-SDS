using EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.HttpClients.ApiClients;
using Microsoft.EntityFrameworkCore;
using static EEMOCantilanSDS.IntegrationTests.SpaceTestKit;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The Stall Rental lifecycle end to end over HTTP for TCC, NCC, BBQ and Ice Plant: the portal's real Stalls client against the real API controllers, JWT and
/// middleware on scratch PostgreSQL. These are the EXISTING stall commands (this work did not change them): Resume, Renew, Assign-new-stall and Remove, with the
/// shared lifecycle read model reporting the state each one leaves behind.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StallLifecycleHttpTests(PostgresFixture db)
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed record World(Guid Tenant, Guid Closed, Guid Lapsed, Guid ForRemoval, Guid ReLetStall, Guid PastContract);

    private async Task<World> SeedAsync(FacilityCode code)
    {
        var tenant = await SeedTenantAsync(db, $"Stall HTTP {code}");
        var facility = Facility.Create(code, code.ToString(), code.ToString(), municipalityId: tenant);
        Stall NewStall(string no) => Stall.Create(facility.Id, no, 900m, ApplicableFees.BaseRental, createdBy: "test", municipalityId: tenant);
        Contract Term(Stall s, string who, DateOnly from, int years) => Contract.Create(s.Id, who, who, from, years, 900m, createdBy: "test");
        var closed = NewStall("1"); closed.Close(new DateOnly(2026, 8, 30), "test");
        var lapsed = NewStall("2");
        var forRemoval = NewStall("3"); forRemoval.Close(new DateOnly(2026, 8, 30), "test");
        var reLet = NewStall("4");
        var past = Term(reLet, "Past Lessee", new DateOnly(2020, 1, 1), 1);
        await using var context = db.CreateContext(tenant);
        context.Add(facility);
        context.AddRange(closed, lapsed, forRemoval, reLet, Term(closed, "Closed Lessee", new DateOnly(2025, 1, 1), 5), Term(lapsed, "Lapsed Lessee", new DateOnly(2020, 1, 1), 1),
            Term(forRemoval, "Removed Lessee", new DateOnly(2025, 1, 1), 5), past, Term(reLet, "Current Lessee", new DateOnly(2025, 1, 1), 5));
        await context.SaveChangesAsync();
        return new(tenant, closed.Id, lapsed.Id, forRemoval.Id, reLet.Id, past.Id);
    }

    [SkippableTheory]
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.NCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    public async Task ResumeRenewAssignAndRemove_RunThroughTheExistingStallCommands_AndTheSharedReadModelFollows(FacilityCode code)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(code);
        await using var head = await CollectorApiHost.StartAsync(db, User, w.Tenant, role: "SuperAdmin");
        var stalls = new StallsApiClient(head.Client);
        var lifecycle = new LifecycleApiClient(head.Client);

        // The register states each account's state; the lifecycle read model agrees with it.
        var register = (await stalls.GetClosedStallAccountsAsync()).Value!;
        Assert.Contains(register, r => r.StallId == w.Closed && r.State == InactiveAccountState.Closed);
        Assert.Contains(register, r => r.StallId == w.Lapsed && r.State is InactiveAccountState.Expired or InactiveAccountState.Lapsed);
        Assert.Contains(register, r => r.StallId == w.ReLetStall && r.StallReLet && r.ContractId == w.PastContract);
        var closedView = (await lifecycle.GetAsync(LifecycleSource.Stall, w.Closed)).Value!;
        Assert.Equal(LifecycleState.Closed, closedView.State);
        Assert.True(closedView.Actions.Single(a => a.Action == LifecycleAction.Resume).Allowed);

        // Resume: the same lessee goes back in the same stall.
        Assert.True((await stalls.ToggleStallStatusAsync(w.Closed, close: false)).IsSuccess);
        Assert.Equal(LifecycleState.Active, (await lifecycle.GetAsync(LifecycleSource.Stall, w.Closed)).Value!.State);
        Assert.DoesNotContain((await stalls.GetClosedStallAccountsAsync()).Value!, r => r.StallId == w.Closed && r.State == InactiveAccountState.Closed);

        // Renew: a lapsed term gets a new one; the account is no longer a lapsed term.
        var lapsedView = (await lifecycle.GetAsync(LifecycleSource.Stall, w.Lapsed)).Value!;
        Assert.True(lapsedView.Actions.Single(a => a.Action == LifecycleAction.Renew).Allowed);
        var renewed = await stalls.RenewStallContractAsync(w.Lapsed, new RenewStallContractRequest(PhilippineToday(), 3, "Lapsed Lessee", "Lapsed Lessee"));
        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.False((await lifecycle.GetAsync(LifecycleSource.Stall, w.Lapsed)).Value!.Actions.Single(a => a.Action == LifecycleAction.Renew).Allowed);

        // Assign new stall: the past occupant of a stall someone else now holds is placed in a stall of their own; the old account is untouched.
        var preview = await stalls.GetStallReassignmentPreviewAsync(w.ReLetStall, w.PastContract);
        Assert.True(preview.IsSuccess, preview.Error);
        var assigned = await stalls.AssignPastOccupantStallAsync(new AssignPastOccupantStallCommand(w.ReLetStall, "44", PhilippineToday().ToDateTime(TimeOnly.MinValue), 3, preview.Value!.MonthlyRate, "Past Lessee", w.PastContract));
        Assert.True(assigned.IsSuccess, assigned.Error);
        await using (var verify = db.CreateContext(w.Tenant))
        {
            Assert.Equal(1, await verify.Stalls.CountAsync(s => s.StallNo == "44"));
            Assert.Equal(2, await verify.Contracts.CountAsync(c => c.StallId == w.ReLetStall));          // the old stall keeps both of its terms
        }

        // Remove: Head only, and soft: the stall and its terms are kept.
        await using (var admin = await CollectorApiHost.StartAsync(db, User, w.Tenant, role: "Admin"))
            Assert.Equal(ResultStatus.Forbidden, (await new StallsApiClient(admin.Client).SoftDeleteStallAsync(w.ForRemoval)).Status);
        Assert.True((await stalls.SoftDeleteStallAsync(w.ForRemoval)).IsSuccess);
        await using var after = db.CreateContext(w.Tenant);
        Assert.Equal(1, await after.Stalls.IgnoreQueryFilters().CountAsync(s => s.Id == w.ForRemoval));      // not hard-deleted
        Assert.Equal(1, await after.Contracts.IgnoreQueryFilters().CountAsync(c => c.StallId == w.ForRemoval));
        Assert.Equal(0, await after.Collections.CountAsync());                                               // no collection was created by any lifecycle action
    }

    private static DateOnly PhilippineToday() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
}
