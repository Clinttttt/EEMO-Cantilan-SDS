using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.HttpClients.ApiClients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EEMOCantilanSDS.IntegrationTests.SpaceTestKit;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The whole journey over HTTP: the portal's real <see cref="LifecycleApiClient"/> against the real API controllers, JWT authentication and
/// middleware on scratch PostgreSQL. Only the transport is the test server.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaceLifecycleHttpTests(PostgresFixture db)
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private async Task<(CollectorApiHost Host, LifecycleApiClient Client)> StartAsync(Guid tenant, string role = "SuperAdmin")
    {
        var host = await CollectorApiHost.StartAsync(db, User, tenant, role: role, clock: new BusinessClock(Today));
        return (host, new LifecycleApiClient(host.Client));
    }

    [SkippableTheory]
    [InlineData(EEMOCantilanSDS.Domain.Enums.ObligationKind.KanmanggaySpaceRental)]
    [InlineData(EEMOCantilanSDS.Domain.Enums.ObligationKind.VegetableFruitSpaceRental)]
    public async Task ClosedAccountToExecute_Resume_ThenReplay_ThenTheSuccessorIsTheOpenOccupancy_AndHistoryIsKept(EEMOCantilanSDS.Domain.Enums.ObligationKind kind)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Http Journey");
        var closed = await AddAsync(db, tenant, "12", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), kind: kind);
        await AssessAsync(db, tenant, closed.Account, closed.Rate, new DateOnly(2026, 2, 1));       // the old holder's unpaid month
        await using var pair = await StartAsyncDisposable(tenant); var client = pair.Client;

        // Closed Accounts: one batched read gives the eligibility the report shows.
        var batch = await client.GetManyAsync(LifecycleSource.SpaceRental, [closed.Account.Id, Guid.NewGuid()]);
        Assert.True(batch.IsSuccess, batch.Error);
        var view = Assert.Single(batch.Value!);
        Assert.Equal((LifecycleState.Closed, true), (view.State, view.Actions.Single(a => a.Action == LifecycleAction.Resume).Allowed));

        // Operations profile: preview, then confirm with one operation id.
        var command = new LifecycleCommand(LifecycleAction.Resume, Jul1, "Same holder returns", "Letter 1");
        var preview = await client.PreviewAsync(LifecycleSource.SpaceRental, closed.Account.Id, command);
        Assert.True(preview.IsSuccess && preview.Value!.CanExecute, preview.Error ?? preview.Value?.Explanation);
        var op = Guid.NewGuid();
        var executed = await client.ExecuteAsync(LifecycleSource.SpaceRental, closed.Account.Id, new(op, preview.Value!.Version, command));
        Assert.True(executed.IsSuccess, executed.Error);
        var successorId = executed.Value!.ResultAccountId!.Value;
        Assert.NotEqual(closed.Account.Id, successorId);                                        // the UI opens the NEW occupancy, not the closed predecessor

        // A retry of the same intent is replayed, never applied twice.
        var retry = await client.ExecuteAsync(LifecycleSource.SpaceRental, closed.Account.Id, new(op, preview.Value.Version, command));
        Assert.True(retry.IsSuccess && retry.Value!.Replayed, retry.Error);
        Assert.Equal(successorId, retry.Value.ResultAccountId);

        // The refreshed views: the successor is open, the predecessor is closed with its history and its own debt.
        var successor = (await client.GetAsync(LifecycleSource.SpaceRental, successorId)).Value!;
        Assert.Equal((LifecycleState.Active, "12", "Ana Reyes"), (successor.State, successor.Label, successor.Holder));
        var predecessor = (await client.GetAsync(LifecycleSource.SpaceRental, closed.Account.Id)).Value!;
        Assert.Equal(LifecycleState.Superseded, predecessor.State);                          // closed, and someone now holds the space
        Assert.Contains(predecessor.History, h => h.Kind.Contains("Resume", StringComparison.OrdinalIgnoreCase) || h.Description.Contains("Resume", StringComparison.OrdinalIgnoreCase));
        await using var verify = db.CreateContext(tenant);
        Assert.Equal(2, await verify.ObligationAccounts.CountAsync());
        Assert.Equal(1, await verify.ObligationPeriods.CountAsync());                            // nothing was billed for the closed gap or moved to the successor
        Assert.Equal(1, await verify.SpaceLifecycleEvents.CountAsync());
        // The predecessor can no longer be resumed; the server says why.
        var again = await client.PreviewAsync(LifecycleSource.SpaceRental, closed.Account.Id, command with { EffectiveDate = new DateOnly(2026, 9, 1) });
        Assert.Equal(SpaceLifecycleWorkflow.SuccessorExists, again.Value!.ReasonCode);
    }

    [SkippableFact]
    public async Task StaleReviewRenewAndRetire_BehaveOverHttp_AndAnAdminCanPreviewButNeverConfirm()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Http Rules");
        var closed = await AddAsync(db, tenant, "40", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), "Ana Reyes");
        var signed = await AddAsync(db, tenant, "41", new DateOnly(2021, 1, 1), signed: true);
        var clean = await AddAsync(db, tenant, "42", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), rateFrom: new DateOnly(2026, 11, 1));
        var owing = await AddAsync(db, tenant, "43", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await using var pair = await StartAsyncDisposable(tenant); var client = pair.Client;

        // Stale: the account changes between review and confirm.
        var reassign = new LifecycleCommand(LifecycleAction.Reassign, Jul1, "New holder", NewHolder: "Pedro Cruz", Amount: 1500m);
        var version = (await client.PreviewAsync(LifecycleSource.SpaceRental, closed.Account.Id, reassign)).Value!.Version;
        await AssessAsync(db, tenant, closed.Account, closed.Rate, new DateOnly(2026, 3, 1));
        var stale = await client.ExecuteAsync(LifecycleSource.SpaceRental, closed.Account.Id, new(Guid.NewGuid(), version, reassign));
        Assert.Equal((false, ResultStatus.Conflict), (stale.IsSuccess, stale.Status));
        Assert.StartsWith(SpaceLifecycleWorkflow.StalePreview, stale.Error);

        // Renew: continuous term recorded as evidence only.
        var renew = new LifecycleCommand(LifecycleAction.Renew, new DateOnly(2024, 1, 1), "Renewal", "Res. 7", TermYears: 3, ContractName: "Signed Lessee");
        var renewPreview = await client.PreviewAsync(LifecycleSource.SpaceRental, signed.Account.Id, renew);
        var renewed = await client.ExecuteAsync(LifecycleSource.SpaceRental, signed.Account.Id, new(Guid.NewGuid(), renewPreview.Value!.Version, renew));
        Assert.True(renewed.IsSuccess, renewed.Error);
        await using (var verify = db.CreateContext(tenant))
        {
            Assert.Equal(1, await verify.SpaceContractTerms.CountAsync());
            Assert.Equal(1, await verify.ObligationRates.CountAsync(r => r.ObligationAccountId == signed.Account.Id));   // the approved rate is untouched
        }

        // Retire: a settled account is retired (records kept); one that owes is refused with its reason.
        var retire = new LifecycleCommand(LifecycleAction.Remove, null, "Space removed from the roll", "Memo 4");
        var cleanPreview = await client.PreviewAsync(LifecycleSource.SpaceRental, clean.Account.Id, retire);
        Assert.True((await client.ExecuteAsync(LifecycleSource.SpaceRental, clean.Account.Id, new(Guid.NewGuid(), cleanPreview.Value!.Version, retire))).IsSuccess);
        var blocked = await client.PreviewAsync(LifecycleSource.SpaceRental, owing.Account.Id, retire);
        Assert.Equal((false, SpaceLifecycleWorkflow.OutstandingBalance), (blocked.Value!.CanExecute, blocked.Value.ReasonCode));
        await using (var verify = db.CreateContext(tenant))
            Assert.NotNull((await verify.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == clean.Account.Id)).RetiredAtUtc);

        // An Admin may confirm a reassignment (as for a stall) but never retire an account.
        await using var adminPair = await StartAsyncDisposable(tenant, "Admin"); var admin = adminPair.Client;
        var adminPreview = await admin.PreviewAsync(LifecycleSource.SpaceRental, closed.Account.Id, reassign);
        Assert.True(adminPreview.IsSuccess);
        var adminDone = await admin.ExecuteAsync(LifecycleSource.SpaceRental, closed.Account.Id, new(Guid.NewGuid(), adminPreview.Value!.Version, reassign));
        Assert.True(adminDone.IsSuccess, adminDone.Error);
        var retireOwing = await admin.ExecuteAsync(LifecycleSource.SpaceRental, owing.Account.Id, new(Guid.NewGuid(), "x", retire));
        Assert.Equal(ResultStatus.Forbidden, retireOwing.Status);
    }

    [SkippableFact]
    public async Task AnotherMunicipalityCannotSeePreviewOrExecuteOnTheAccount_AndAnAccountStillRunningOffersNothingToResume()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Http Isolation");
        var other = await SeedTenantAsync(db, "Http Other");
        var closed = await AddAsync(db, tenant, "12", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var ending = await AddAsync(db, tenant, "13", new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1));      // an end date that has not arrived
        await using var pair = await StartAsyncDisposable(tenant); var client = pair.Client;
        var command = new LifecycleCommand(LifecycleAction.Resume, Jul1, "Same holder returns");

        // A future end date is still an active account: nothing is offered to resume it.
        var view = (await client.GetAsync(LifecycleSource.SpaceRental, ending.Account.Id)).Value!;
        Assert.Equal(LifecycleState.Ending, view.State);
        Assert.False(view.Actions.Single(a => a.Action == LifecycleAction.Resume).Allowed);
        Assert.True((await client.GetAsync(LifecycleSource.SpaceRental, closed.Account.Id)).Value!.Actions.Single(a => a.Action == LifecycleAction.Resume).Allowed);

        await using var foreignPair = await StartAsyncDisposable(other); var foreign = foreignPair.Client;
        Assert.Equal(ResultStatus.NotFound, (await foreign.GetAsync(LifecycleSource.SpaceRental, closed.Account.Id)).Status);
        Assert.Equal(ResultStatus.NotFound, (await foreign.PreviewAsync(LifecycleSource.SpaceRental, closed.Account.Id, command)).Status);
        Assert.Equal(ResultStatus.NotFound, (await foreign.ExecuteAsync(LifecycleSource.SpaceRental, closed.Account.Id, new(Guid.NewGuid(), "x", command))).Status);
        Assert.Empty((await foreign.GetManyAsync(LifecycleSource.SpaceRental, [closed.Account.Id])).Value!);
    }

    // The host is the disposable half; the pair keeps the client beside it.
    private async Task<DisposablePair> StartAsyncDisposable(Guid tenant, string role = "SuperAdmin")
    {
        var (host, client) = await StartAsync(tenant, role);
        return new(host, client);
    }

    private sealed class DisposablePair(CollectorApiHost host, LifecycleApiClient client) : IAsyncDisposable
    {
        public LifecycleApiClient Client => client;
        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
