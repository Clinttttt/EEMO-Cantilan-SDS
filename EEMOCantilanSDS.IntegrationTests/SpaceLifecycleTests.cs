using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static EEMOCantilanSDS.IntegrationTests.SpaceTestKit;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Phase 3: Resume, Renew, Reassign and Retire of a monthly space account, against real PostgreSQL. Every refusal is by a stated reason; nothing is guessed.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaceLifecycleTests(PostgresFixture db)
{
    private static readonly DateOnly Closed = new(2026, 6, 30);     // closed on the last day of June -> earliest successor start is Jul 1
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private static LifecycleCommand Resume(DateOnly? start = null, string reason = "Same holder returns") => new(LifecycleAction.Resume, start ?? Jul1, reason, "Letter 1");
    private static LifecycleCommand Reassign(string holder = "Pedro Cruz", DateOnly? start = null, decimal? amount = 1500m, bool confirm = false, Guid? payor = null) =>
        new(LifecycleAction.Reassign, start ?? Jul1, "New holder", "Contract 2", NewHolder: holder, Amount: amount, ConfirmDistinctHolder: confirm, NewPayorId: payor);

    private async Task<Result<LifecyclePreviewDto>> PreviewAsync(Guid tenant, Guid accountId, LifecycleCommand command, string? role = "SuperAdmin")
    {
        await using var context = db.CreateContext(tenant);
        return await Workflow(context, tenant, role).PreviewAsync(accountId, command);
    }

    private async Task<Result<LifecycleOutcome>> ExecuteAsync(Guid tenant, Guid accountId, LifecycleCommand command, Guid? op = null, string? version = null,
        string? role = "SuperAdmin")
    {
        await using var context = db.CreateContext(tenant);
        var workflow = Workflow(context, tenant, role);
        version ??= (await workflow.PreviewAsync(accountId, command)).Value!.Version;
        return await workflow.ExecuteAsync(accountId, new(op ?? Guid.NewGuid(), version, command));
    }

    private static async Task<string> PreviewVersionAsync(PostgresFixture db, Guid tenant, Guid accountId, LifecycleCommand command)
    {
        await using var context = db.CreateContext(tenant);
        return (await Workflow(context, tenant).PreviewAsync(accountId, command)).Value!.Version;
    }

    private async Task<(Guid Tenant, Occupancy Closed)> ClosedSpaceAsync(string number = "12", string holder = "Ana Reyes", DateOnly? closedOn = null)
    {
        var tenant = await SeedTenantAsync(db, "Lifecycle Tenant");
        return (tenant, await AddAsync(db, tenant, number, new DateOnly(2026, 1, 1), closedOn ?? Closed, holder));
    }

    private static string CodeOf(Result<LifecycleOutcome> r) => r.Error![..r.Error!.IndexOf(':')];

    // ── authority: the same as a stall's ──

    [SkippableFact]
    public async Task AdminOrHeadMayResumeRenewAndReassign_LikeForAStall_ButOnlyTheHeadRetires_AndNobodyElseDoes()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Authority");
        var toResume = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), Closed);
        var toReassign = await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1), Closed);
        var toRenew = await AddAsync(db, tenant, "3", new DateOnly(2021, 1, 1), signed: true);                 // term 2021-01-01 .. 2023-12-31 has run out
        var toRetire = await AddAsync(db, tenant, "4", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), rateFrom: new DateOnly(2026, 11, 1));
        LifecycleCommand renew = new(LifecycleAction.Renew, new DateOnly(2024, 1, 1), "Renewal", TermYears: 3);
        LifecycleCommand retire = new(LifecycleAction.Remove, null, "Space removed from the roll", "Memo 4");

        Assert.True((await ExecuteAsync(tenant, toResume.Account.Id, Resume(), role: "Admin")).IsSuccess);
        Assert.True((await ExecuteAsync(tenant, toReassign.Account.Id, Reassign(), role: "Admin")).IsSuccess);
        Assert.True((await ExecuteAsync(tenant, toRenew.Account.Id, renew, role: "Admin")).IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, (await ExecuteAsync(tenant, toRetire.Account.Id, retire, role: "Admin")).Status);
        Assert.True((await ExecuteAsync(tenant, toRetire.Account.Id, retire, role: "SuperAdmin")).IsSuccess);
        foreach (var action in new[] { Resume(), Reassign(), renew, retire })
        {
            Assert.Equal(ResultStatus.Forbidden, (await PreviewAsync(tenant, toResume.Account.Id, action, role: "Collector")).Status);
            Assert.Equal(ResultStatus.Forbidden, (await ExecuteAsync(tenant, toResume.Account.Id, action, version: "x", role: "Collector")).Status);
        }
    }


    // ── Resume ──

    [SkippableFact]
    public async Task Resume_CreatesALinkedSuccessor_WithTheSameHolderAndCarriedRate_AndLeavesThePredecessorUntouched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();
        await AssessAsync(db, tenant, closed.Account, closed.Rate, new DateOnly(2026, 2, 1));       // an unpaid month stays with the old occupancy

        var preview = await PreviewAsync(tenant, closed.Account.Id, Resume());
        Assert.True(preview.Value!.CanExecute, preview.Value.Explanation);
        Assert.Equal("₱1,200.00", preview.Value.Facts["Approved monthly amount"]);

        await using var before = db.CreateContext(tenant);
        var predecessorBefore = await before.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == closed.Account.Id);
        var periodsBefore = await before.ObligationPeriods.AsNoTracking().Select(p => new { p.Id, p.AssessedAmount, p.ObligationAccountId }).ToListAsync();

        var op = Guid.NewGuid();
        var result = await ExecuteAsync(tenant, closed.Account.Id, Resume(), op, preview.Value.Version);
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.Replayed);

        await using var after = db.CreateContext(tenant);
        var successor = await after.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == result.Value.ResultAccountId);
        Assert.Equal((closed.Registration.Id, closed.Account.Id, Jul1, null, "Ana Reyes", "12"),
            (successor.SpaceRegistrationId!.Value, successor.PredecessorAccountId!.Value, successor.ActiveFrom, successor.ActiveTo, successor.ActualOccupant, successor.SubjectLabel));
        Assert.Equal(1200m, (await after.ObligationRates.AsNoTracking().SingleAsync(r => r.ObligationAccountId == successor.Id)).Amount);
        Assert.Equal(Jul1, (await after.ObligationRates.AsNoTracking().SingleAsync(r => r.ObligationAccountId == successor.Id)).EffectiveFrom);
        // No back-billing: the successor has no period, the predecessor's closure and money are exactly as they were.
        Assert.Equal(0, await after.ObligationPeriods.CountAsync(p => p.ObligationAccountId == successor.Id));
        var predecessorAfter = await after.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == closed.Account.Id);
        Assert.Equal((predecessorBefore.ActiveTo, predecessorBefore.ActiveFrom, predecessorBefore.ActualOccupant, predecessorBefore.RetiredAtUtc),
            (predecessorAfter.ActiveTo, predecessorAfter.ActiveFrom, predecessorAfter.ActualOccupant, predecessorAfter.RetiredAtUtc));
        Assert.Equal(periodsBefore, await after.ObligationPeriods.AsNoTracking().Select(p => new { p.Id, p.AssessedAmount, p.ObligationAccountId }).ToListAsync());
        var evt = await after.SpaceLifecycleEvents.AsNoTracking().SingleAsync();
        Assert.Equal((SpaceLifecycleAction.Resume, closed.Account.Id, successor.Id, op), (evt.Action, evt.SourceAccountId, evt.ResultAccountId, evt.ClientOperationId));
        Assert.Equal(64, evt.IntentFingerprint.Length);
        // The predecessor's balance is still its own.
        var quotesAfter = await new EEMOCantilanSDS.Application.Common.Revenue.ObligationCollectionSource(after).GetQuotesAsync(tenant, [predecessorAfter], Closed, default, lookbackMonths: 480);
        var quotesBefore = await new EEMOCantilanSDS.Application.Common.Revenue.ObligationCollectionSource(before).GetQuotesAsync(tenant, [predecessorBefore], Closed, default, lookbackMonths: 480);
        Assert.True(quotesBefore.Sum(q => q.OutstandingAmount) > 0m);
        Assert.Equal(quotesBefore.Sum(q => q.OutstandingAmount), quotesAfter.Sum(q => q.OutstandingAmount));
    }

    [SkippableFact]
    public async Task Resume_IsBlockedWithAReason_WhenTheStartIsMidMonth_InTheClosureMonth_OrTheAccountIsNotClosedOrAlreadyHasASuccessor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync(closedOn: new DateOnly(2026, 6, 15));      // closed mid-June
        var id = closed.Account.Id;

        Assert.Equal(SpaceLifecycleWorkflow.MidMonthStartPending, (await PreviewAsync(tenant, id, Resume(new DateOnly(2026, 7, 15)))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.ClosureMonthOwnershipPending, (await PreviewAsync(tenant, id, Resume(new DateOnly(2026, 6, 1)))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.MidMonthStartPending, (await PreviewAsync(tenant, id, Resume(new DateOnly(2026, 6, 16)))).Value!.ReasonCode);
        var blocked = await ExecuteAsync(tenant, id, Resume(new DateOnly(2026, 7, 15)), version: "x");
        Assert.NotEqual(CodeOf(blocked), SpaceLifecycleWorkflow.StalePreview);                // evaluated before the version is compared
        Assert.True((await PreviewAsync(tenant, id, Resume(new DateOnly(2026, 7, 1)))).Value!.CanExecute);

        // An end date that has not arrived yet: the account is still active and billed to it, so there is nothing to resume.
        var ending = await AddAsync(db, tenant, "21", new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1));
        var endingPreview = await PreviewAsync(tenant, ending.Account.Id, Resume(new DateOnly(2026, 12, 1)));
        Assert.Equal(SpaceLifecycleWorkflow.NotClosed, endingPreview.Value!.ReasonCode);
        Assert.Contains("Nov 1, 2026", endingPreview.Value.Explanation);

        // Open account: nothing to resume.
        var open = await AddAsync(db, tenant, "20", new DateOnly(2026, 1, 1));
        Assert.Equal(SpaceLifecycleWorkflow.NotClosed, (await PreviewAsync(tenant, open.Account.Id, Resume())).Value!.ReasonCode);

        // After a successor exists, the predecessor cannot be resumed again.
        Assert.True((await ExecuteAsync(tenant, id, Resume(new DateOnly(2026, 8, 1)))).IsSuccess);
        Assert.Equal(SpaceLifecycleWorkflow.SuccessorExists, (await PreviewAsync(tenant, id, Resume(new DateOnly(2026, 9, 1)))).Value!.ReasonCode);
    }

    [SkippableFact]
    public async Task Resume_NeverInventsAnAmount_WhenTheClosedAccountHasNone()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "No Rate");
        var registration = SpaceRegistration.Create(tenant, ObligationKind.KanmanggaySpaceRental, "5", "test");
        var account = ObligationAccount.Create(tenant, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "5", null, null, new DateOnly(2026, 1, 1), "test", actualOccupant: "No Rate Holder");
        account.LinkRegistration(registration.Id);
        account.Close(Closed);
        await using (var context = db.CreateContext(tenant)) { context.SpaceRegistrations.Add(registration); context.ObligationAccounts.Add(account); await context.SaveChangesAsync(); }
        Assert.Equal(SpaceLifecycleWorkflow.NoApprovedAmount, (await PreviewAsync(tenant, account.Id, Resume())).Value!.ReasonCode);
    }

    // ── idempotency, staleness, one winner ──

    [SkippableFact]
    public async Task ARetry_ReplaysTheSameOutcome_AChangedRequestWithTheSameOperationIsAConflict_AndAStalePreviewIsRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();
        var op = Guid.NewGuid();
        var first = await ExecuteAsync(tenant, closed.Account.Id, Resume(), op);
        Assert.True(first.IsSuccess, first.Error);

        var replay = await ExecuteAsync(tenant, closed.Account.Id, Resume(), op, version: "ignored-on-replay");
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.True(replay.Value!.Replayed);
        Assert.Equal((first.Value!.EventId, first.Value.ResultAccountId), (replay.Value.EventId, replay.Value.ResultAccountId));

        var different = await ExecuteAsync(tenant, closed.Account.Id, Resume(new DateOnly(2026, 9, 1), "Another reason"), op, version: "x");
        Assert.Equal((false, ResultStatus.Conflict, SpaceLifecycleWorkflow.OperationConflict), (different.IsSuccess, different.Status, CodeOf(different)));

        await using var verify = db.CreateContext(tenant);
        Assert.Equal(2, await verify.ObligationAccounts.CountAsync());                          // exactly one successor
        Assert.Equal(1, await verify.SpaceLifecycleEvents.CountAsync());

        // A stale preview: the state changed (a different space fact) between preview and confirm.
        var other = await AddAsync(db, tenant, "40", new DateOnly(2026, 1, 1), Closed);
        var version = await PreviewVersionAsync(db, tenant, other.Account.Id, Resume());
        await AssessAsync(db, tenant, other.Account, other.Rate, new DateOnly(2026, 3, 1));      // changes what the confirmation was based on
        var stale = await ExecuteAsync(tenant, other.Account.Id, Resume(), version: version);
        Assert.Equal((ResultStatus.Conflict, SpaceLifecycleWorkflow.StalePreview), (stale.Status, CodeOf(stale)));
        await using var after = db.CreateContext(tenant);
        Assert.Equal(0, await after.ObligationAccounts.CountAsync(a => a.PredecessorAccountId == other.Account.Id));
    }

    [SkippableFact]
    public async Task ConcurrentResumeAndReassign_OfTheSameClosedAccount_HaveExactlyOneWinner()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();
        var resume = Resume();
        var reassign = Reassign();
        // Both actors preview the same state, then confirm at the same time with different operation ids.
        var resumeVersion = await PreviewVersionAsync(db, tenant, closed.Account.Id, resume);
        var reassignVersion = await PreviewVersionAsync(db, tenant, closed.Account.Id, reassign);
        var results = await Task.WhenAll(
            ExecuteAsync(tenant, closed.Account.Id, resume, version: resumeVersion),
            ExecuteAsync(tenant, closed.Account.Id, reassign, version: reassignVersion),
            ExecuteAsync(tenant, closed.Account.Id, resume, version: resumeVersion));
        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(ResultStatus.Conflict, r.Status));

        await using var verify = db.CreateContext(tenant);
        Assert.Equal(1, await verify.ObligationAccounts.CountAsync(a => a.PredecessorAccountId == closed.Account.Id));
        Assert.Equal(1, await verify.SpaceLifecycleEvents.CountAsync());
        Assert.Equal(2, await verify.ObligationAccounts.CountAsync());
    }

    // ── Reassign ──

    [SkippableFact]
    public async Task Reassign_CreatesADistinctOccupancy_WithTheStatedHolderAndAmount_AndThePredecessorKeepsItsOwnDebt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();
        await AssessAsync(db, tenant, closed.Account, closed.Rate, new DateOnly(2026, 2, 1));

        var result = await ExecuteAsync(tenant, closed.Account.Id, Reassign());
        Assert.True(result.IsSuccess, result.Error);
        await using var after = db.CreateContext(tenant);
        var successor = await after.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == result.Value!.ResultAccountId);
        Assert.Equal(("Pedro Cruz", closed.Registration.Id, closed.Account.Id, Jul1), (successor.ActualOccupant, successor.SpaceRegistrationId!.Value, successor.PredecessorAccountId!.Value, successor.ActiveFrom));
        Assert.Equal(1500m, (await after.ObligationRates.AsNoTracking().SingleAsync(r => r.ObligationAccountId == successor.Id)).Amount);
        // The old holder's debt is not moved to the new holder.
        Assert.Equal(0, await after.ObligationPeriods.CountAsync(p => p.ObligationAccountId == successor.Id));
        Assert.Equal(1, await after.ObligationPeriods.CountAsync(p => p.ObligationAccountId == closed.Account.Id));
        Assert.Equal("Ana Reyes", (await after.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == closed.Account.Id)).ActualOccupant);
    }

    [SkippableFact]
    public async Task Reassign_UnderASignedContract_RecordsTheNewHoldersOwnTerm_AsTheRegistrationFormDoes()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();

        // The preview states the previous approved amount so the form can start from it.
        var first = await PreviewAsync(tenant, closed.Account.Id, new(LifecycleAction.Reassign, null, "New holder"));
        Assert.Equal("₱1,200.00", first.Value!.Facts["Previous approved amount"]);
        // A term is a signed contract's: refused on a space-only basis.
        var spaceOnlyWithTerm = new LifecycleCommand(LifecycleAction.Reassign, Jul1, "New holder", NewHolder: "Pedro Cruz", Amount: 1500m, TermYears: 3,
            Arrangement: OccupancyArrangement.SpaceOnly);
        Assert.Equal(SpaceLifecycleWorkflow.InvalidRequest, (await PreviewAsync(tenant, closed.Account.Id, spaceOnlyWithTerm)).Value!.ReasonCode);

        var signed = new LifecycleCommand(LifecycleAction.Reassign, Jul1, "New holder", "Contract 2", NewHolder: "Pedro Cruz", Amount: 1500m, TermYears: 3,
            ContractName: "Pedro Cruz", Reference: "KM-9", Arrangement: OccupancyArrangement.SignedContract);
        var result = await ExecuteAsync(tenant, closed.Account.Id, signed);
        Assert.True(result.IsSuccess, result.Error);
        await using var after = db.CreateContext(tenant);
        var successor = await after.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == result.Value!.ResultAccountId);
        Assert.Equal((OccupancyArrangement.SignedContract, "KM-9", "Pedro Cruz", Jul1, 3),
            (successor.Arrangement!.Value, successor.ContractReference, successor.ContractName, successor.ContractEffectiveDate!.Value, successor.ContractYears!.Value));
    }

    [SkippableFact]
    public async Task Reassign_RequiresAnExplicitHolderAndAmount_RefusesASameNameWithoutConfirmation_AndALinkToAnotherTenantsPayor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();
        var id = closed.Account.Id;
        Assert.Equal(SpaceLifecycleWorkflow.HolderRequired, (await PreviewAsync(tenant, id, Reassign(holder: "  "))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.ApprovedAmountRequired, (await PreviewAsync(tenant, id, Reassign(amount: null))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.ApprovedAmountRequired, (await PreviewAsync(tenant, id, Reassign(amount: 0m))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.ApprovedAmountRequired, (await PreviewAsync(tenant, id, Reassign(amount: 10.005m))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.SameHolderName, (await PreviewAsync(tenant, id, Reassign(holder: " ana reyes "))).Value!.ReasonCode);
        Assert.True((await PreviewAsync(tenant, id, Reassign(holder: "Ana Reyes", confirm: true))).Value!.CanExecute);
        Assert.Equal(SpaceLifecycleWorkflow.InvalidRequest, (await PreviewAsync(tenant, id, Reassign(payor: Guid.NewGuid()))).Value!.ReasonCode);   // no such Payor in this tenant
        Assert.Equal(SpaceLifecycleWorkflow.MidMonthStartPending, (await PreviewAsync(tenant, id, Reassign(start: new DateOnly(2026, 7, 10)))).Value!.ReasonCode);
    }

    // ── Renew ──

    [SkippableFact]
    public async Task Renew_RecordsTheNextTermAsEvidence_OnlyOnceTheTermHasRunOut_AndTouchesNoRateOrPeriod()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Renew");
        var open = await AddAsync(db, tenant, "12", new DateOnly(2021, 1, 1), signed: true);          // term 2021-01-01 .. 2023-12-31 has run out
        await AssessAsync(db, tenant, open.Account, open.Rate, new DateOnly(2026, 2, 1));
        LifecycleCommand Renew(DateOnly commence, int years = 3) => new(LifecycleAction.Renew, commence, "Renewal", "Res. 7", TermYears: years, ContractName: "Signed Lessee", Reference: "KM-REF-2");

        Assert.Equal(SpaceLifecycleWorkflow.RenewalOverlapsTerm, (await PreviewAsync(tenant, open.Account.Id, Renew(new DateOnly(2023, 12, 31)))).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.RenewalOverlapsTerm, (await PreviewAsync(tenant, open.Account.Id, Renew(new DateOnly(2022, 1, 1)))).Value!.ReasonCode);

        await using (var before = db.CreateContext(tenant))
        {
            var ratesBefore = await before.ObligationRates.AsNoTracking().CountAsync();
            var periodsBefore = await before.ObligationPeriods.AsNoTracking().CountAsync();
            var renewed = await ExecuteAsync(tenant, open.Account.Id, Renew(new DateOnly(2024, 1, 1)));       // the day after the old term
            Assert.True(renewed.IsSuccess, renewed.Error);
            await using var after = db.CreateContext(tenant);
            Assert.Equal((ratesBefore, periodsBefore), (await after.ObligationRates.CountAsync(), await after.ObligationPeriods.CountAsync()));
            Assert.Equal(1, await after.ObligationAccounts.CountAsync());                       // a renewal opens no account
            var term = await after.SpaceContractTerms.AsNoTracking().SingleAsync();
            Assert.Equal((new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31)), (term.Commencement, term.LastDay));

            // The register reports the term in force, so the renewed space reads as current, not "term ended" for ever; the account's
            // own recorded term is left as it was.
            var listed = await new EEMOCantilanSDS.Application.Common.Revenue.ObligationWorkflow(after, new Caller(tenant, "SuperAdmin"), new Tenant(tenant))
                .GetAccountsAsync(open.Account.Kind);
            var dto = Assert.Single(listed.Value!);
            Assert.Equal((new DateOnly(2024, 1, 1), 3, "Signed Lessee", "KM-REF-2"), (dto.ContractEffectiveDate, dto.ContractYears, dto.ContractName, dto.ContractReference));
            Assert.Equal(new DateOnly(2021, 1, 1), (await after.ObligationAccounts.AsNoTracking().SingleAsync()).ContractEffectiveDate);
        }
        // The new term runs to the end of 2026, so it cannot be renewed again yet (the same rule as a stall's running term).
        var again = await PreviewAsync(tenant, open.Account.Id, Renew(new DateOnly(2027, 1, 1)));
        Assert.Equal(LifecycleReason.TermStillRunning, again.Value!.ReasonCode);
    }

    [SkippableFact]
    public async Task ARenewedSpace_StatesEachTermsOwnBalance_TheTermsAddBackToTheAccount_AndFollowUpCarriesOnlyTheCurrentTerm()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Renewed terms");
        var open = await AddAsync(db, tenant, "7", new DateOnly(2021, 1, 1), signed: true, amount: 1000m);       // term 2021-01-01 .. 2023-12-31
        foreach (var month in new[] { new DateOnly(2023, 11, 1), new DateOnly(2023, 12, 1), new DateOnly(2024, 2, 1), new DateOnly(2026, 8, 1) })
            await AssessAsync(db, tenant, open.Account, open.Rate, month);
        var other = await AddAsync(db, tenant, "8", new DateOnly(2021, 1, 1), signed: true, amount: 500m);       // expired, never renewed
        await AssessAsync(db, tenant, other.Account, other.Rate, new DateOnly(2023, 12, 1));

        var renewed = await ExecuteAsync(tenant, open.Account.Id,
            new(LifecycleAction.Renew, new DateOnly(2024, 1, 1), "Renewal", "Res. 9", TermYears: 3, ContractName: "Signed Lessee", Reference: "KM-REF-3"));
        Assert.True(renewed.IsSuccess, renewed.Error);

        await using var context = db.CreateContext(tenant);
        var head = new Caller(tenant, "SuperAdmin");
        var listed = (await new EEMOCantilanSDS.Application.Common.Revenue.ObligationWorkflow(context, head, new Tenant(tenant), new BusinessClock(Today))
            .GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!;
        var dto = listed.Single(x => x.Id == open.Account.Id);
        var terms = dto.Terms!;
        Assert.Equal(2, terms.Count);
        var (earlier, current) = (terms[0], terms[1]);
        Assert.Equal((new DateOnly(2021, 1, 1), new DateOnly(2023, 12, 31), false), (earlier.Commencement, earlier.RenewedOn!.Value, earlier.Current));
        Assert.Equal((new DateOnly(2024, 1, 1), (DateOnly?)null, true), (current.Commencement, current.RenewedOn, current.Current));
        var register = (await new EEMOCantilanSDS.Application.Common.Revenue.ObligationWorkflow(context, head, new Tenant(tenant), new BusinessClock(Today))
            .GetRegisterAsync(open.Account.Id)).Value!;
        var renewal = new DateOnly(2024, 1, 1);
        Assert.Equal(register.Where(q => q.PeriodStart < renewal).Sum(q => q.OutstandingAmount), earlier.Outstanding);   // up to Dec 2023, the earlier term's own
        Assert.Equal(register.Where(q => q.PeriodStart >= renewal).Sum(q => q.OutstandingAmount), current.Outstanding);
        Assert.True(earlier.Outstanding >= 2000m && current.Outstanding >= 2000m);
        // Every period is in exactly one term: nothing is double counted and nothing is lost.
        Assert.Equal((dto.AssessedToDate, dto.CollectedToDate, dto.OutstandingToDate),
            (terms.Sum(t => t.Assessed), terms.Sum(t => t.Collected), terms.Sum(t => t.Outstanding)));
        Assert.Null(listed.Single(x => x.Id == other.Account.Id).Terms);                                     // never renewed: read as one term, as before

        var followUp = await new EEMOCantilanSDS.Application.Common.Revenue.SpaceFollowUpWorkflow(context, head, new Tenant(tenant)).GetAsync(Today, null, default);
        // The never-renewed expired account: every month is Arrears (expired contract), with the term's last day stated; nothing is removed.
        var expired = followUp.Where(x => x.AccountId == other.Account.Id).ToList();
        Assert.All(expired, x => Assert.Equal(("arrears", "Expired contract", false, new DateOnly(2023, 12, 31)), (x.ReasonKind, x.ArrearsBasis, x.EarlierTerm, x.TermLastDay!.Value)));
        // The renewed account's current term (2024-2026) is still running on Oct 19, 2026: its 2026 months are Delinquent and its earlier years' Arrears (previous year).
        Assert.Equal("delinquent", followUp.Single(x => x.AccountId == open.Account.Id && x.PeriodStart == new DateOnly(2026, 8, 1)).ReasonKind);
        Assert.Equal(("arrears", "Previous year"), followUp.Where(x => x.AccountId == open.Account.Id && x.PeriodStart == new DateOnly(2024, 2, 1))
            .Select(x => (x.ReasonKind, x.ArrearsBasis!)).Single());
        // The whole-time view states the earlier term too, flagged and classified as an expired contract, and the two views never overlap.
        var whole = await new EEMOCantilanSDS.Application.Common.Revenue.SpaceFollowUpWorkflow(context, head, new Tenant(tenant)).GetAsync(Today, null, default, includeEarlierTerms: true);
        var earlierRows = whole.Where(x => x.AccountId == open.Account.Id && x.EarlierTerm).ToList();
        Assert.NotEmpty(earlierRows);
        Assert.All(earlierRows, x => Assert.True(x.PeriodStart < new DateOnly(2024, 1, 1) && x.ArrearsBasis == "Expired contract"));
        Assert.Equal(earlier.Outstanding, earlierRows.Sum(x => x.Outstanding));
        Assert.Equal(whole.Count - earlierRows.Count, followUp.Count);
        var renewedFollowUp = followUp.Where(x => x.AccountId == open.Account.Id).Select(x => x.PeriodStart).ToList();
        Assert.Contains(new DateOnly(2024, 2, 1), renewedFollowUp);                                          // the current term's debt is followed up
        Assert.Contains(new DateOnly(2026, 8, 1), renewedFollowUp);
        Assert.DoesNotContain(renewedFollowUp, p => p < renewal);                                            // the earlier term's debt is not
        Assert.Contains(followUp, x => x.AccountId == other.Account.Id && x.PeriodStart == new DateOnly(2023, 12, 1));   // an expired, non-renewed debt still is
        // Nothing was rewritten to present it this way.
        Assert.Equal(5, await context.ObligationPeriods.CountAsync());
        Assert.Equal(new DateOnly(2021, 1, 1), (await context.ObligationAccounts.AsNoTracking().SingleAsync(x => x.Id == open.Account.Id)).ContractEffectiveDate);
    }

    [SkippableFact]
    public async Task Renew_IsNotOfferedForAClosedAccount_AnUnsignedOccupancy_OrAnEventLot()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Renew Blocks");
        var closed = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), Closed, signed: true);
        var unsigned = await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1));
        LifecycleCommand renew = new(LifecycleAction.Renew, new DateOnly(2029, 1, 1), "Renewal", TermYears: 3);
        Assert.Equal(SpaceLifecycleWorkflow.AccountClosed, (await PreviewAsync(tenant, closed.Account.Id, renew)).Value!.ReasonCode);
        var running = await AddAsync(db, tenant, "5", new DateOnly(2026, 1, 1), signed: true);                // a signed term that is still running
        Assert.Equal(LifecycleReason.TermStillRunning, (await PreviewAsync(tenant, running.Account.Id, renew)).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.NoSignedContract, (await PreviewAsync(tenant, unsigned.Account.Id, renew)).Value!.ReasonCode);

        var lot = ObligationAccount.Create(tenant, ObligationKind.FiestaArawLotRental, Guid.Empty, null, "3", LotRentalEvent.Fiesta, new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15), "test", actualOccupant: "Lot");
        await using (var context = db.CreateContext(tenant)) { context.ObligationAccounts.Add(lot); await context.SaveChangesAsync(); }
        foreach (var action in new[] { LifecycleAction.Resume, LifecycleAction.Renew, LifecycleAction.Reassign, LifecycleAction.Remove })
            Assert.Equal("NotApplicableToEventLot", (await PreviewAsync(tenant, lot.Id, new(action, Jul1, "x", NewHolder: "Y", Amount: 1m, TermYears: 1))).Value!.ReasonCode);
    }

    // ── Retire ──

    [SkippableFact]
    public async Task Retire_IsAudited_NonDestructive_BlockedByAnyBalance_AndRetiredAccountsCannotResume()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Retire");
        var owing = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), Closed);
        await AssessAsync(db, tenant, owing.Account, owing.Rate, new DateOnly(2026, 2, 1));
        var clean = await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), rateFrom: new DateOnly(2026, 11, 1));   // no approved amount ever applied: nothing was ever owed
        LifecycleCommand retire = new(LifecycleAction.Remove, null, "Space removed from the roll", "Memo 4");

        var blocked = await ExecuteAsync(tenant, owing.Account.Id, retire);
        Assert.Equal((ResultStatus.Conflict, SpaceLifecycleWorkflow.OutstandingBalance), (blocked.Status, CodeOf(blocked)));
        Assert.Equal(LifecycleReason.StillActive, (await PreviewAsync(tenant, (await AddAsync(db, tenant, "3", new DateOnly(2026, 1, 1))).Account.Id, retire)).Value!.ReasonCode);

        // Only a SuperAdmin retires.
        Assert.Equal(ResultStatus.Forbidden, (await ExecuteAsync(tenant, clean.Account.Id, retire, role: "Admin")).Status);

        var done = await ExecuteAsync(tenant, clean.Account.Id, retire);
        Assert.True(done.IsSuccess, done.Error);
        await using var after = db.CreateContext(tenant);
        var retired = await after.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == clean.Account.Id);    // not deleted
        Assert.NotNull(retired.RetiredAtUtc);
        Assert.Equal(new DateOnly(2026, 6, 30), retired.ActiveTo);
        Assert.Equal(1, await after.ObligationRates.CountAsync(r => r.ObligationAccountId == clean.Account.Id));
        Assert.Equal(SpaceLifecycleAction.Retire, (await after.SpaceLifecycleEvents.AsNoTracking().SingleAsync()).Action);
        Assert.Equal(SpaceLifecycleWorkflow.Retired, (await PreviewAsync(tenant, clean.Account.Id, Resume())).Value!.ReasonCode);
        Assert.Equal(SpaceLifecycleWorkflow.Retired, (await PreviewAsync(tenant, clean.Account.Id, retire)).Value!.ReasonCode);
    }

    // ── tenant, role, source ──

    [SkippableFact]
    public async Task TenantAndRoleBoundaries_AreEnforced()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, closed) = await ClosedSpaceAsync();
        var other = await SeedTenantAsync(db, "Other Tenant");

        // Another municipality neither sees nor changes this account.
        Assert.Equal(ResultStatus.NotFound, (await PreviewAsync(other, closed.Account.Id, Resume())).Status);
        Assert.Equal(ResultStatus.NotFound, (await ExecuteAsync(other, closed.Account.Id, Resume(), version: "x")).Status);

        // Roles: Admin may preview, only SuperAdmin may execute, others neither; a token claiming another municipality is refused.
        Assert.True((await PreviewAsync(tenant, closed.Account.Id, Resume(), role: "Admin")).IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, (await PreviewAsync(tenant, closed.Account.Id, Resume(), role: "Collector")).Status);
        Assert.Equal(ResultStatus.Forbidden, (await ExecuteAsync(tenant, closed.Account.Id, Resume(), version: "x", role: "Collector")).Status);
        await using (var context = db.CreateContext(tenant))
        {
            var wrongClaim = Workflow(context, tenant, "SuperAdmin", claimedTenant: other);
            Assert.Equal(ResultStatus.Forbidden, (await wrongClaim.ExecuteAsync(closed.Account.Id, new(Guid.NewGuid(), "x", Resume()))).Status);
            var anonymous = Workflow(context, tenant, "SuperAdmin", authenticated: false);
            Assert.Equal(ResultStatus.Unauthorized, (await anonymous.PreviewAsync(closed.Account.Id, Resume())).Status);
        }
        await using var verify = db.CreateContext(tenant);
        Assert.Equal(0, await verify.SpaceLifecycleEvents.CountAsync());
        Assert.Equal(1, await verify.ObligationAccounts.CountAsync());
    }

    [SkippableFact]
    public async Task OperationIdsAreScopedPerTenant_AndStallsHaveNoExecutor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (a, closedA) = await ClosedSpaceAsync();
        var (b, closedB) = await ClosedSpaceAsync();
        var op = Guid.NewGuid();
        Assert.True((await ExecuteAsync(a, closedA.Account.Id, Resume(), op)).IsSuccess);
        var inB = await ExecuteAsync(b, closedB.Account.Id, Resume(), op);                       // the same id in another municipality is a different operation
        Assert.True(inB.IsSuccess, inB.Error);
        Assert.False(inB.Value!.Replayed);

        await using var context = db.CreateContext(a);
        var space = new SpaceAccountLifecycle(context, new Tenant(a), Workflow(context, a));
        var resolver = new AccountLifecycleResolver([space], [space]);
        Assert.NotNull(resolver.ExecutorFor(LifecycleSource.SpaceRental));
        Assert.Null(resolver.ExecutorFor(LifecycleSource.Stall));                                // stalls keep their own reviewed flows
    }
    [SkippableFact]
    public async Task TheBatchReadReturnsOnlyTheCallersAccounts_AndRefusesAnEmptyOrOversizedRequest()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (a, closedA) = await ClosedSpaceAsync();
        var (b, closedB) = await ClosedSpaceAsync();
        await using var context = db.CreateContext(a);
        var workflow = Workflow(context, a, "SuperAdmin");
        var adapter = new SpaceAccountLifecycle(context, new Tenant(a), workflow);
        var handler = new EEMOCantilanSDS.Application.Queries.Accounts.GetAccountLifecycle.GetAccountLifecycleBatchQueryHandler(
            new AccountLifecycleResolver([adapter], [adapter]), new Caller(a, "Admin"), new Tenant(a));

        var result = await handler.Handle(new(LifecycleSource.SpaceRental, [closedA.Account.Id, closedB.Account.Id, Guid.NewGuid()]), default);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([closedA.Account.Id], result.Value!.Select(x => x.AccountId).ToArray());          // the other municipality's account and an unknown id are simply absent
        Assert.Equal(ResultStatus.Invalid, (await handler.Handle(new(LifecycleSource.SpaceRental, []), default)).Status);
        Assert.Equal(ResultStatus.Invalid, (await handler.Handle(new(LifecycleSource.SpaceRental, Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList()), default)).Status);
        var collector = new EEMOCantilanSDS.Application.Queries.Accounts.GetAccountLifecycle.GetAccountLifecycleBatchQueryHandler(
            new AccountLifecycleResolver([adapter], [adapter]), new Caller(a, "Collector"), new Tenant(a));
        Assert.Equal(ResultStatus.Forbidden, (await collector.Handle(new(LifecycleSource.SpaceRental, [closedA.Account.Id]), default)).Status);
    }

}
