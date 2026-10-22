using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Accounts.GetAccountLifecycle;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The shared lifecycle foundation (Phase 1, read-only). Eligibility is server-authoritative and mirrors the EXISTING source paths: these
/// tests pin the permissions the current controllers grant, the guards the current commands enforce, and the distinction between sources.
/// </summary>
public class AccountLifecycleTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly FixedClock Clock = new(new DateTime(2026, 10, 19, 4, 0, 0, DateTimeKind.Utc));

    private static AllowedAction Of(IReadOnlyList<AllowedAction> actions, LifecycleAction a) => actions.Single(x => x.Action == a);

    // ── audited permissions: characterization of the CURRENT controllers (any change here is an authorization change) ──

    [Fact]
    public void ExistingServerPermissions_AreExactlyWhatTheLifecycleRulesReport()
    {
        var stalls = typeof(StallsController);
        Assert.Equal("SuperAdmin,Admin", stalls.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles);
        // Close/Resume (status), Renew and AssignPastOccupant carry no narrower attribute: Admin or SuperAdmin.
        foreach (var name in new[] { nameof(StallsController.ToggleStatus), nameof(StallsController.RenewContract), nameof(StallsController.AssignPastOccupantStall) })
            Assert.Empty(stalls.GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), false));
        // Removal is the Head's alone.
        Assert.Equal("SuperAdmin", stalls.GetMethod(nameof(StallsController.SoftDeleteStall))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
        // A space account's Close is the Head's alone.
        Assert.Equal("SuperAdmin", typeof(ObligationsController).GetMethod(nameof(ObligationsController.CloseAsync))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
    }

    [Theory]
    [InlineData("SuperAdmin", true, true)]
    [InlineData("Admin", true, false)]      // an Admin may close/resume/renew a stall today, but not remove one
    [InlineData("Collector", false, false)]
    [InlineData(null, false, false)]
    public void StallRoles_FollowTheExistingControllers(string? role, bool mayOperate, bool mayRemove)
    {
        var closedExpired = new StallLifecycleFacts(true, true, true, false, false);
        var actions = LifecycleRules.ForStall(closedExpired, role);
        Assert.Equal(mayOperate, Of(actions, LifecycleAction.View).Allowed);
        Assert.Equal(mayOperate, Of(actions, LifecycleAction.Resume).Allowed);
        Assert.Equal(mayRemove, Of(actions, LifecycleAction.Remove).Allowed);
        if (!mayOperate) Assert.Equal(LifecycleReason.RoleRequired, Of(actions, LifecycleAction.Resume).ReasonCode);
    }

    // ── stall eligibility mirrors the current commands ──

    [Fact]
    public void ActiveStall_CanBeClosed_NotResumedOrRemoved_AndAnAlreadyClosedOneIsAdvisoryOnly()
    {
        var active = LifecycleRules.ForStall(new(false, false, false, false, false), "SuperAdmin");
        Assert.True(Of(active, LifecycleAction.Close).Allowed);
        Assert.Equal(LifecycleReason.NotClosed, Of(active, LifecycleAction.Resume).ReasonCode);
        Assert.False(Of(active, LifecycleAction.Resume).Enforced);                        // the command would still run: reported honestly
        Assert.Equal(LifecycleReason.StillActive, Of(active, LifecycleAction.Remove).ReasonCode);
        Assert.True(Of(active, LifecycleAction.Remove).Enforced);                         // SoftDeleteStall refuses a stall still in term

        var closed = LifecycleRules.ForStall(new(true, false, false, false, false), "SuperAdmin");
        Assert.Equal(LifecycleReason.AlreadyClosed, Of(closed, LifecycleAction.Close).ReasonCode);
        Assert.False(Of(closed, LifecycleAction.Close).Enforced);
        Assert.True(Of(closed, LifecycleAction.Resume).Allowed);
        Assert.True(Of(closed, LifecycleAction.Remove).Allowed);
    }

    [Fact]
    public void ResumeIsBlockedByAClosedSectionAndByActivatedMonthEvidence_WhichTheServerEnforces()
    {
        var section = Of(LifecycleRules.ForStall(new(true, false, false, true, false), "Admin"), LifecycleAction.Resume);
        Assert.False(section.Allowed); Assert.Equal(LifecycleReason.SectionClosed, section.ReasonCode); Assert.True(section.Enforced);
        var evidence = LifecycleRules.ForStall(new(true, false, false, false, true), "Admin");
        Assert.Equal(LifecycleReason.MonthEvidenceActivated, Of(evidence, LifecycleAction.Resume).ReasonCode);
        Assert.Equal(LifecycleReason.MonthEvidenceActivated, Of(LifecycleRules.ForStall(new(false, false, false, false, true), "Admin"), LifecycleAction.Close).ReasonCode);
    }

    [Fact]
    public void Renew_IsAdvertisedOnlyForAnExpiredOpenTerm_AndTheServersLackOfAGuardIsReportedHonestly()
    {
        // Intended eligibility is what Closed Accounts offers: a term that has run out on an account that is still open.
        var expired = LifecycleRules.ForStall(new(false, true, false, false, false), "Admin");
        Assert.True(Of(expired, LifecycleAction.Renew).Allowed);
        Assert.False(Of(expired, LifecycleAction.Renew).Enforced);                        // the command has no state guard of its own

        // A running term: the command would accept an early renewal (or a handover), but nothing offers it, so it is not advertised.
        var running = Of(LifecycleRules.ForStall(new(false, false, false, false, false), "Admin"), LifecycleAction.Renew);
        Assert.False(running.Allowed);
        Assert.Equal(LifecycleReason.TermStillRunning, running.ReasonCode);
        Assert.False(running.Enforced);

        // A closed account is resumed, not renewed.
        var closed = Of(LifecycleRules.ForStall(new(true, true, false, false, false), "SuperAdmin"), LifecycleAction.Renew);
        Assert.Equal(LifecycleReason.AccountClosed, closed.ReasonCode);
        Assert.False(Of(LifecycleRules.ForStall(new(false, true, false, false, false), "Collector"), LifecycleAction.Renew).Allowed);
    }

    [Fact]
    public void Reassign_IsAdvertisedOnlyForAPastOccupancyOfAStallNowHeldBySomeoneElse()
    {
        Assert.True(Of(LifecycleRules.ForStall(new(false, false, true, false, false), "Admin"), LifecycleAction.Reassign).Allowed);
        var notReLet = Of(LifecycleRules.ForStall(new(false, true, false, false, false), "Admin"), LifecycleAction.Reassign);
        Assert.False(notReLet.Allowed);
        Assert.Equal(LifecycleReason.NotReLet, notReLet.ReasonCode);
        Assert.False(notReLet.Enforced);
    }

    [Fact]
    public void Remove_IsHeadOnly_AndOnlyForAClosedOrExpiredAccount()
    {
        Assert.False(Of(LifecycleRules.ForStall(new(false, true, false, false, false), "Admin"), LifecycleAction.Remove).Allowed);       // Admin: Head only
        Assert.True(Of(LifecycleRules.ForStall(new(false, true, false, false, false), "SuperAdmin"), LifecycleAction.Remove).Allowed);
        Assert.False(Of(LifecycleRules.ForStall(new(false, false, false, false, false), "SuperAdmin"), LifecycleAction.Remove).Allowed);
    }

    // ── space sources: the same authority and state rules as a stall ──

    private static SpaceLifecycleFacts Space(ObligationKind kind = ObligationKind.KanmanggaySpaceRental, bool registered = true, bool ended = false, DateOnly? endsOn = null,
        bool retired = false, bool successor = false, bool signed = false, DateOnly? termLast = null) =>
        new(kind, registered, ended, endsOn, retired, successor, signed, termLast, new DateOnly(2026, 10, 19));

    [Theory]
    [InlineData(ObligationKind.KanmanggaySpaceRental)]
    [InlineData(ObligationKind.VegetableFruitSpaceRental)]
    public void AnEndedSpaceNobodyHolds_ReadsLikeAClosedStall_AdminResumesAndReassigns_OnlyTheHeadRemoves(ObligationKind kind)
    {
        var ended = Space(kind, ended: true);
        foreach (var role in new[] { "Admin", "SuperAdmin" })
        {
            var actions = LifecycleRules.ForSpace(ended, role);
            Assert.True(Of(actions, LifecycleAction.Resume).Allowed);
            Assert.True(Of(actions, LifecycleAction.Reassign).Allowed);
            Assert.Equal(LifecycleReason.AccountClosed, Of(actions, LifecycleAction.Renew).ReasonCode);        // a closed account is resumed, not renewed
        }
        Assert.Equal(LifecycleReason.RoleRequired, Of(LifecycleRules.ForSpace(ended, "Admin"), LifecycleAction.Remove).ReasonCode);
        Assert.True(Of(LifecycleRules.ForSpace(ended, "SuperAdmin"), LifecycleAction.Remove).Allowed);
        Assert.Equal(LifecycleReason.RoleRequired, Of(LifecycleRules.ForSpace(ended, "Admin"), LifecycleAction.Close).ReasonCode);
        Assert.Equal(LifecycleReason.AlreadyClosed, Of(LifecycleRules.ForSpace(ended, "SuperAdmin"), LifecycleAction.Close).ReasonCode);
        Assert.Equal(LifecycleReason.RoleRequired, Of(LifecycleRules.ForSpace(ended, "Collector"), LifecycleAction.Resume).ReasonCode);
    }

    [Fact]
    public void SpaceAndStallAgreeOnEveryStateTheyShare()
    {
        // The stall's and the space's states that mean the same thing must offer the same Resume / Renew / Remove, for each role.
        var today = new DateOnly(2026, 10, 19);
        var cases = new (string Name, StallLifecycleFacts Stall, SpaceLifecycleFacts Space)[]
        {
            ("closed", new(true, false, false, false, false), Space(ended: true)),
            ("open, term run out", new(false, true, false, false, false), Space(signed: true, termLast: today.AddDays(-30))),
            ("open, term running", new(false, false, false, false, false), Space(signed: true, termLast: today.AddDays(400))),
        };
        foreach (var (name, stall, space) in cases)
            foreach (var role in new[] { "Admin", "SuperAdmin", "Collector" })
                foreach (var action in new[] { LifecycleAction.Resume, LifecycleAction.Renew, LifecycleAction.Remove })
                {
                    // One deliberate difference: a stall whose term has run out can be removed while open; a space is still billed monthly, so it is closed first.
                    if (action == LifecycleAction.Remove && name == "open, term run out") continue;
                    Assert.True(Of(LifecycleRules.ForStall(stall, role), action).Allowed == Of(LifecycleRules.ForSpace(space, role), action).Allowed, $"{name} / {role} / {action}");
                }
    }

    [Fact]
    public void ARenewalNeedsASignedTermThatHasRunOut_AResumeNeedsAnEndedAccount_AndAFutureEndDateIsStillActive()
    {
        var today = new DateOnly(2026, 10, 19);
        Assert.True(Of(LifecycleRules.ForSpace(Space(signed: true, termLast: today.AddDays(-1)), "Admin"), LifecycleAction.Renew).Allowed);
        Assert.Equal(LifecycleReason.TermStillRunning, Of(LifecycleRules.ForSpace(Space(signed: true, termLast: today), "Admin"), LifecycleAction.Renew).ReasonCode);
        Assert.Equal("NoSignedContract", Of(LifecycleRules.ForSpace(Space(signed: false), "Admin"), LifecycleAction.Renew).ReasonCode);   // a space-only occupancy has no term to renew
        Assert.Equal("NoRecordedTerm", Of(LifecycleRules.ForSpace(Space(signed: true, termLast: null), "Admin"), LifecycleAction.Renew).ReasonCode);

        var ending = LifecycleRules.ForSpace(Space(endsOn: new DateOnly(2026, 11, 1)), "SuperAdmin");
        Assert.Equal(LifecycleReason.NotClosed, Of(ending, LifecycleAction.Resume).ReasonCode);
        Assert.Contains("Nov 1, 2026", Of(ending, LifecycleAction.Resume).Reason);
        Assert.True(Of(ending, LifecycleAction.Close).Allowed);                                              // still active, so it can still be closed
        Assert.Equal("SuccessorExists", Of(LifecycleRules.ForSpace(Space(ended: true, successor: true), "Admin"), LifecycleAction.Resume).ReasonCode);
        Assert.Equal("Retired", Of(LifecycleRules.ForSpace(Space(ended: true, retired: true), "SuperAdmin"), LifecycleAction.Resume).ReasonCode);
    }

    [Fact]
    public void AnAccountNotYetLinkedToItsSpace_SaysSoBriefly_AndAnEventLotIsNeverARenewableLease()
    {
        var unlinked = LifecycleRules.ForSpace(Space(registered: false, ended: true), "SuperAdmin");
        Assert.Equal("NoSpaceRegistration", Of(unlinked, LifecycleAction.Resume).ReasonCode);
        Assert.True(Of(unlinked, LifecycleAction.Resume).Reason!.Length < 70);
        var lot = LifecycleRules.ForSpace(Space(ObligationKind.FiestaArawLotRental, ended: true), "SuperAdmin");
        foreach (var a in new[] { LifecycleAction.Resume, LifecycleAction.Renew, LifecycleAction.Reassign, LifecycleAction.Remove })
            Assert.Equal(LifecycleReason.NotApplicableToEventLot, Of(lot, a).ReasonCode);
    }

    // ── adapters: tenant boundary, source-kind distinction, no writes ──

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static ObligationAccount Account(Guid tenant, ObligationKind kind, string label, DateOnly from, DateOnly? eventDate = null)
    {
        var account = ObligationAccount.Create(tenant, kind, Guid.Empty, null, label,
            kind == ObligationKind.FiestaArawLotRental ? LotRentalEvent.Fiesta : null, eventDate, from, "tester", actualOccupant: "Ana Reyes");
        return account;
    }

    [Fact]
    public async Task SpaceAdapter_ReadsOnlyTheCallersTenant_AndDistinguishesMonthlySpaceFromEventLot()
    {
        await using var db = NewContext();
        var mine = Account(Tenant, ObligationKind.KanmanggaySpaceRental, "12", new DateOnly(2026, 1, 1));
        var foreign = Account(OtherTenant, ObligationKind.KanmanggaySpaceRental, "12", new DateOnly(2026, 1, 1));
        var lot = Account(Tenant, ObligationKind.FiestaArawLotRental, "7", new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15));
        db.ObligationAccounts.AddRange(mine, foreign, lot);
        await db.SaveChangesAsync();
        var adapter = new SpaceAccountLifecycle(db, new FixedMunicipality(Tenant), new SpaceLifecycleWorkflow(db, new Caller("SuperAdmin"), new FixedMunicipality(Tenant), Clock));

        var view = await adapter.GetAsync(LifecycleSource.SpaceRental, mine.Id, "SuperAdmin", default);
        Assert.NotNull(view);
        Assert.Equal((LifecycleSource.SpaceRental, "12", "Ana Reyes", LifecycleState.Active), (view!.Source, view.Label, view.Holder, view.State));
        Assert.True(Of(view.Actions, LifecycleAction.Close).Allowed);

        Assert.Null(await adapter.GetAsync(LifecycleSource.SpaceRental, foreign.Id, "SuperAdmin", default));       // another municipality's account is not found
        Assert.Null(await adapter.GetAsync(LifecycleSource.SpaceRental, lot.Id, "SuperAdmin", default));           // an event lot is never read as a monthly lease
        Assert.Null(await adapter.GetAsync(LifecycleSource.EventLot, mine.Id, "SuperAdmin", default));
        var lotView = await adapter.GetAsync(LifecycleSource.EventLot, lot.Id, "SuperAdmin", default);
        Assert.Equal(LifecycleReason.NotApplicableToEventLot, Of(lotView!.Actions, LifecycleAction.Renew).ReasonCode);
    }

    [Fact]
    public async Task ClosedSpaceAccount_ShowsItsClosureInHistory_AndAFishMeatVendorFeeIsNotALifecycleSource()
    {
        await using var db = NewContext();
        var closed = Account(Tenant, ObligationKind.VegetableFruitSpaceRental, "3", new DateOnly(2026, 2, 1));
        closed.Close(new DateOnly(2026, 6, 30));
        db.ObligationAccounts.Add(closed);
        await db.SaveChangesAsync();
        var adapter = new SpaceAccountLifecycle(db, new FixedMunicipality(Tenant), new SpaceLifecycleWorkflow(db, new Caller("SuperAdmin"), new FixedMunicipality(Tenant), Clock));

        var view = await adapter.GetAsync(LifecycleSource.SpaceRental, closed.Id, "Admin", default);
        Assert.Equal(LifecycleState.Closed, view!.State);
        Assert.Equal(new DateOnly(2026, 6, 30), view.EndedOn);
        Assert.Contains(view.History, h => h.Kind == "Closed" && h.Date == new DateOnly(2026, 6, 30));
        Assert.Equal(LifecycleReason.RoleRequired, Of(view.Actions, LifecycleAction.Close).ReasonCode);             // an Admin cannot close a space account
        Assert.Equal("NoSpaceRegistration", Of(view.Actions, LifecycleAction.Resume).ReasonCode);             // not yet linked to its physical space
    }

    private static (Stall Stall, Contract Term) StallWithTerm(DateOnly start, int years, bool closed = false, string? section = null)
    {
        var stall = Stall.Create(Guid.NewGuid(), "5", 900m, ApplicableFees.BaseRental, customSectionName: section);
        var term = Contract.Create(stall.Id, "Lisa Cruz", "Lisa Cruz", start, years, 900m);
        stall.Contracts.Add(term);
        if (closed) stall.Close(new DateOnly(2026, 8, 30));
        return (stall, term);
    }

    private static (StallLifecycle Adapter, Mock<IStallRepository> Repo) StallAdapter(Stall? stall, AppDbContext db)
    {
        var repo = new Mock<IStallRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdWithContractsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => stall is not null && stall.Id == id ? stall : null);
        repo.Setup(r => r.GetFacilityCodeByStallIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(FacilityCode.NPM);
        return (new StallLifecycle(repo.Object, db, Clock), repo);
    }

    [Fact]
    public async Task StallAdapter_ReadsStateAndHistory_ChangesNothing_AndNeverReturnsAnotherTenantsStall()
    {
        await using var db = NewContext();
        var (stall, _) = StallWithTerm(new DateOnly(2023, 1, 1), 3);              // term ran out 2025-12-31 → expired while still open
        var (adapter, repo) = StallAdapter(stall, db);

        var view = await adapter.GetAsync(LifecycleSource.Stall, stall.Id, "Admin", default);
        Assert.Equal((LifecycleSource.Stall, "5", "NPM", "Lisa Cruz", LifecycleState.TermExpired), (view!.Source, view.Label, view.SourceDetail, view.Holder, view.State));
        Assert.Contains(view.History, h => h.Kind == "TermStarted" && h.Date == new DateOnly(2023, 1, 1));
        Assert.True(Of(view.Actions, LifecycleAction.Renew).Allowed);
        Assert.False(Of(view.Actions, LifecycleAction.Remove).Allowed);            // an Admin may not remove
        // The repository's tenant/soft-delete filter decides visibility: an id it cannot see is simply not found.
        Assert.Null(await adapter.GetAsync(LifecycleSource.Stall, Guid.NewGuid(), "Admin", default));
        // Strict mock: only the two reads are set up, so any update/save/delete call would have thrown.
        repo.Verify(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StallAdapter_ReadsReLetAndRunningTerms_ThroughTheSameEligibilityTheRegisterUses()
    {
        await using var db = NewContext();
        // A stall let to Wilma until mid-June 2026 and to Teofila since: Wilma's past occupancy is re-let, the running term is not renewable yet.
        var stall = Stall.Create(Guid.NewGuid(), "7", 1_000m, ApplicableFees.BaseRental);
        var outgoing = Contract.Create(stall.Id, "Wilma K. Tecson", "Wilma K. Tecson", new DateOnly(2026, 1, 1), 1, 1_000m);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 15));
        stall.Contracts.Add(outgoing);
        stall.Contracts.Add(Contract.Create(stall.Id, "Teofila Reyes", "Teofila Reyes", new DateOnly(2026, 6, 16), 3, 1_000m));
        var (adapter, _) = StallAdapter(stall, db);

        var view = await adapter.GetAsync(LifecycleSource.Stall, stall.Id, "Admin", default);
        Assert.Equal(("Teofila Reyes", LifecycleState.Active), (view!.Holder, view.State));
        Assert.True(Of(view.Actions, LifecycleAction.Reassign).Allowed);
        Assert.Equal(LifecycleReason.TermStillRunning, Of(view.Actions, LifecycleAction.Renew).ReasonCode);
        Assert.Contains(view.History, h => h.Kind == "TermEnded" && h.Date == new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task ClosedStallInAClosedSection_IsReportedAsNotResumable_BecauseTheServerRefusesIt()
    {
        await using var db = NewContext();
        var (stall, _) = StallWithTerm(new DateOnly(2025, 1, 1), 5, closed: true, section: "Sari-sari Area");
        db.FacilitySectionClosures.Add(FacilitySectionClosure.Create(FacilityCode.NPM, "Sari-sari Area", new DateOnly(2026, 8, 30), new[] { stall.Id }));
        await db.SaveChangesAsync();
        var (adapter, _) = StallAdapter(stall, db);

        var view = await adapter.GetAsync(LifecycleSource.Stall, stall.Id, "SuperAdmin", default);
        Assert.Equal(LifecycleState.Closed, view!.State);
        Assert.Equal(LifecycleReason.SectionClosed, Of(view.Actions, LifecycleAction.Resume).ReasonCode);
        Assert.Equal(new DateOnly(2026, 8, 30), view.EndedOn);
    }

    // ── the query: authentication, role, tenant claim ──

    private sealed class Caller(string? role, Guid? claimedTenant = null, bool authenticated = true) : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => authenticated ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null;
        public string? Username => "tester";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => claimedTenant ?? Tenant;
    }

    private sealed class FakeAdapter(AccountLifecycleDto? dto) : IAccountLifecycle
    {
        public bool Handles(LifecycleSource source) => true;
        public Task<AccountLifecycleDto?> GetAsync(LifecycleSource source, Guid accountId, string? actorRole, CancellationToken ct) => Task.FromResult(dto);
    }

    private static AccountLifecycleDto Dto() => new(LifecycleSource.Stall, Guid.NewGuid(), "1", "NPM", "X", LifecycleState.Active, null, null, null, [], []);

    [Theory]
    [InlineData(null, false, ResultStatus.Unauthorized)]
    [InlineData("Collector", true, ResultStatus.Forbidden)]
    public async Task Query_RefusesTheUnauthenticatedAndCollectors(string? role, bool authenticated, ResultStatus expected)
    {
        var handler = new GetAccountLifecycleQueryHandler(new AccountLifecycleResolver([new FakeAdapter(Dto())]), new Caller(role, authenticated: authenticated), new FixedMunicipality(Tenant));
        Assert.Equal(expected, (await handler.Handle(new(LifecycleSource.Stall, Guid.NewGuid()), default)).Status);
    }

    [Fact]
    public async Task Query_RefusesATokenForAnotherMunicipality_AndReportsNotFoundWithoutRevealingIt()
    {
        var foreignToken = new GetAccountLifecycleQueryHandler(new AccountLifecycleResolver([new FakeAdapter(Dto())]), new Caller("SuperAdmin", OtherTenant), new FixedMunicipality(Tenant));
        Assert.Equal(ResultStatus.Forbidden, (await foreignToken.Handle(new(LifecycleSource.Stall, Guid.NewGuid()), default)).Status);

        var missing = new GetAccountLifecycleQueryHandler(new AccountLifecycleResolver([new FakeAdapter(null)]), new Caller("Admin"), new FixedMunicipality(Tenant));
        Assert.Equal(ResultStatus.NotFound, (await missing.Handle(new(LifecycleSource.Stall, Guid.NewGuid()), default)).Status);

        var ok = new GetAccountLifecycleQueryHandler(new AccountLifecycleResolver([new FakeAdapter(Dto())]), new Caller("Admin"), new FixedMunicipality(Tenant));
        Assert.True((await ok.Handle(new(LifecycleSource.Stall, Guid.NewGuid()), default)).IsSuccess);
    }
}
