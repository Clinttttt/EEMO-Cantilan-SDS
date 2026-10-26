using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Lifecycle;

/// <summary>
/// Lifecycle execution for monthly Space Rentals (Kanmanggay, Vegetable/Fruit): Resume, Renew, Reassign and Retire.
///
/// <para><b>Identity.</b> A physical space is a <see cref="SpaceRegistration"/>; each <see cref="ObligationAccount"/> on it is one occupancy.
/// Resume and Reassign never clear a closure: they append a SUCCESSOR occupancy that begins after the predecessor's last day. The predecessor
/// keeps its rates, periods, allocations, Collections, SRCs and balance, and stays collectible against its own holder. The successor starts
/// with its own empty position. A closed gap is never assessed, because no account is active in it.</para>
///
/// <para><b>Safeguards.</b> Preview and confirm with a state version; a durable tenant-unique client operation id with an intent fingerprint
/// (a retry returns the original outcome, a changed request under the same id conflicts); one tenant-wide advisory lock for the whole
/// operation; database constraints (no overlapping occupancy on a space, one successor per predecessor, append-only evidence) as the final
/// guarantee; the tenant always comes from the authenticated identity.</para>
///
/// <para><b>Eligibility.</b> Structural eligibility (state, role) comes from the one shared rule table <see cref="LifecycleRules.ForSpace"/>, in the same shape as a stall'"'"'s; this
/// class adds only what needs a date, an amount or a balance. Anything ambiguous about which holder owes a month (a mid-month start, a start inside the closure
/// month, a renewal overlapping the recorded term) is blocked with an actionable explanation; the server never guesses an owner. There is no per-LGU switch.</para>
/// </summary>
public sealed class SpaceLifecycleWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock clock)
{
    private DateOnly Today => clock.PhilippineToday;

    // ── codes ──
    public const string NotApplicable = "NotApplicable";
    public const string NoSpaceRegistration = "NoSpaceRegistration";
    public const string NotClosed = "NotClosed";
    public const string AccountClosed = "AccountClosed";
    public const string Retired = "Retired";
    public const string SuccessorExists = "SuccessorExists";
    public const string LaterOccupancyExists = "LaterOccupancyExists";
    public const string MidMonthStartPending = "MidMonthStartPending";
    public const string ClosureMonthOwnershipPending = "ClosureMonthOwnershipPending";
    public const string NoApprovedAmount = "NoApprovedAmount";
    public const string NoVerifiedHolder = "NoVerifiedHolder";
    public const string HolderRequired = "HolderRequired";
    public const string SameHolderName = "SameHolderName";
    public const string ApprovedAmountRequired = "ApprovedAmountRequired";
    public const string NoSignedContract = "NoSignedContract";
    public const string NoRecordedTerm = "NoRecordedTerm";
    public const string RenewalOverlapsTerm = "RenewalOverlapsTerm";
    public const string OutstandingBalance = "OutstandingBalance";
    public const string StalePreview = "StalePreview";
    public const string OperationConflict = "OperationConflict";
    public const string InvalidRequest = "InvalidRequest";

    private sealed record Actor(Guid UserId, Guid TenantId, string Username, string Role);

    private sealed record Space(
        ObligationAccount Account, SpaceRegistration? Registration, IReadOnlyList<ObligationAccount> Occupancies,
        bool HasSuccessor, DateOnly? LastAssessed, IReadOnlyList<ObligationRate> Rates, SpaceContractTerm? LastTerm, int EventCount, string? HolderName);

    private sealed record Evaluation(string? Code, string? Text, string Version, Dictionary<string, string> Facts, Space? Space,
        decimal? Amount = null, DateOnly? Start = null, SpaceContractTerm? PriorTerm = null, DateOnly? PriorLastDay = null);

    // ── authorization / tenant ──
    private Actor? Resolve(string requiredRole, out string? failure)
    {
        failure = null;
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty) { failure = "Unauthorized"; return null; }
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant) { failure = "Forbidden"; return null; }
        if (!LifecycleRules.HasRole(currentUser.Role, requiredRole)) { failure = "Forbidden"; return null; }
        return new Actor(userId, tenant, currentUser.Username ?? "Office User", currentUser.Role!);
    }

    private static Result<T> Refuse<T>(string failure) => failure == "Unauthorized" ? Result<T>.Unauthorized() : Result<T>.Forbidden();

    // ── state ──
    private async Task<Space?> LoadAsync(Guid tenantId, Guid accountId, bool tracked, CancellationToken ct)
    {
        IQueryable<ObligationAccount> accounts = db.ObligationAccounts.Where(x => x.MunicipalityId == tenantId && x.Id == accountId);
        var account = await (tracked ? accounts : accounts.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (account is null) return null;
        SpaceRegistration? registration = null;
        IReadOnlyList<ObligationAccount> occupancies = [account];
        if (account.SpaceRegistrationId is { } registrationId)
        {
            registration = await db.SpaceRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == registrationId, ct);
            occupancies = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.SpaceRegistrationId == registrationId).ToListAsync(ct);
        }
        var hasSuccessor = occupancies.Any(x => x.PredecessorAccountId == account.Id);
        var lastAssessed = await db.ObligationPeriods.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.ObligationAccountId == accountId)
            .Select(x => (DateOnly?)x.PeriodStart).MaxAsync(ct);
        var rates = await db.ObligationRates.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.ObligationAccountId == accountId).ToListAsync(ct);
        var lastTerm = await db.SpaceContractTerms.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.ObligationAccountId == accountId)
            .OrderByDescending(x => x.Commencement).FirstOrDefaultAsync(ct);
        var eventCount = account.SpaceRegistrationId is { } rid
            ? await db.SpaceLifecycleEvents.AsNoTracking().CountAsync(x => x.MunicipalityId == tenantId && x.SpaceRegistrationId == rid, ct) : 0;
        // The holder is named as the office names them everywhere else: the occupant on the account, else its Business Payor.
        var holder = !string.IsNullOrWhiteSpace(account.ActualOccupant) ? account.ActualOccupant.Trim()
            : account.PayorId != Guid.Empty ? await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.Id == account.PayorId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) : null;
        return new Space(account, registration, occupancies, hasSuccessor, lastAssessed, rates, lastTerm, eventCount, holder);
    }

    /// <summary>The state the actor confirms. Everything a lifecycle decision reads about this account and its space is in it.</summary>
    private string VersionOf(Space s)
    {
        var a = s.Account;
        var latest = s.Occupancies.OrderByDescending(x => x.ActiveFrom).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        var raw = string.Join('|', a.Id, a.ActiveFrom.DayNumber, a.ActiveTo?.DayNumber.ToString() ?? "-", a.RetiredAtUtc?.Ticks.ToString() ?? "-",
            a.SpaceRegistrationId?.ToString() ?? "-", latest?.Id.ToString() ?? "-", s.Occupancies.Count, s.HasSuccessor ? 1 : 0,
            s.LastAssessed?.DayNumber.ToString() ?? "-", string.Join(',', s.Rates.OrderBy(r => r.Id).Select(r => r.Id)), s.LastTerm?.Id.ToString() ?? "-", s.EventCount);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string Fingerprint(Guid accountId, LifecycleCommand c)
    {
        var raw = string.Join('|', accountId, (int)c.Action, c.EffectiveDate?.DayNumber.ToString() ?? "-", (c.NewHolder ?? string.Empty).Trim().ToUpperInvariant(),
            c.NewPayorId?.ToString() ?? "-", c.ConfirmDistinctHolder ? 1 : 0, c.Amount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            (c.TermYears?.ToString() ?? "-") + (c.TermMonths is { } tm ? "m" + tm : ""), (c.ContractName ?? string.Empty).Trim(), (c.Reference ?? string.Empty).Trim(), c.Arrangement?.ToString() ?? "-",
            (c.Reason ?? string.Empty).Trim(), (c.Evidence ?? string.Empty).Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static bool TermIsValid(int? years, int? months)
    {
        try { DomainRules.NormalizeSpaceTerm(years, months); return true; }
        catch (ArgumentException) { return false; }
    }

    private static DateOnly FirstOfNextMonth(DateOnly d) => new DateOnly(d.Year, d.Month, 1).AddMonths(1);

    private async Task<decimal> OutstandingAsync(Guid tenantId, ObligationAccount account, CancellationToken ct)
    {
        // The whole history, not the usual five-year window: a debt must never look settled only because it is old.
        var asOf = account.ActiveTo is { } to && to < Today ? to : Today;
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(tenantId, [account], asOf, ct, lookbackMonths: 12 * 40);
        return quotes.Sum(x => x.OutstandingAmount);
    }

    // ── eligibility: the shared rule table, then what needs a date, an amount or a balance ──
    private SpaceLifecycleFacts FactsOf(Space s)
    {
        var a = s.Account;
        DateOnly? lastDay = null;
        if (s.LastTerm is { } term) lastDay = term.LastDay;
        else if (a.ContractEffectiveDate is { } ce && a.ContractYears is { } cy) lastDay = DomainRules.TermLastDay(ce, cy, a.ContractMonths);
        var laterOccupancy = a.ActiveTo is { } closedOn && s.Occupancies.Any(x => x.Id != a.Id && x.ActiveFrom > closedOn);
        return new(a.Kind, s.Registration is not null, RentalAccountStates.HasEnded(a.ActiveTo, Today), a.ActiveTo is { } e && e >= Today ? e : null, a.RetiredAtUtc is not null,
            s.HasSuccessor || laterOccupancy, a.Arrangement == OccupancyArrangement.SignedContract, lastDay, Today);
    }

    /// <summary>The account's lifecycle facts and holder name for the read view, or null when it is not this municipality's.</summary>
    public async Task<(SpaceLifecycleFacts Facts, string? Holder)?> FactsAsync(Guid accountId, CancellationToken ct = default)
    {
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty) return null;
        var space = await LoadAsync(tenant, accountId, tracked: false, ct);
        return space is null ? null : (FactsOf(space), space.HolderName);
    }

    private static LifecycleAction ToLifecycle(SpaceLifecycleAction a) => a switch
    {
        SpaceLifecycleAction.Resume => LifecycleAction.Resume, SpaceLifecycleAction.Renew => LifecycleAction.Renew,
        SpaceLifecycleAction.Reassign => LifecycleAction.Reassign, _ => LifecycleAction.Remove
    };

    private async Task<Evaluation> EvaluateAsync(Actor actor, Space? space, SpaceLifecycleAction action, LifecycleCommand? c, CancellationToken ct)
    {
        var facts = new Dictionary<string, string>();
        if (space is null) return new(NotApplicable, "That account was not found.", "-", facts, null);
        var a = space.Account;
        var version = VersionOf(space);
        Evaluation Block(string code, string text) => new(code, text, version, facts, space);

        facts["Space"] = space.Registration?.DisplayNumber ?? a.SubjectLabel;
        facts["Holder"] = space.HolderName ?? "Not recorded";
        facts["Occupancy"] = a.ActiveTo is { } closed ? $"{a.ActiveFrom:MMM d, yyyy} to {closed:MMM d, yyyy}" : $"Since {a.ActiveFrom:MMM d, yyyy}";

        if (!ObligationAccount.IsMonthlySpaceKind(a.Kind) && a.Kind != ObligationKind.FiestaArawLotRental) return Block(NotApplicable, "Only Kanmanggay and Vegetable/Fruit space accounts have this lifecycle.");
        // One table decides state and role for every rental account; a stall's rules read the same way.
        var factsOf = FactsOf(space);
        var rule = LifecycleRules.ForSpace(factsOf, actor.Role).Single(x => x.Action == ToLifecycle(action));
        if (!rule.Allowed) return Block(rule.ReasonCode ?? NotApplicable, rule.Reason ?? "This is not available for this account.");

        // The outstanding position stays with this occupancy and is shown on every preview.
        var outstanding = await OutstandingAsync(actor.TenantId, a, ct);
        facts["Outstanding balance (stays with this occupancy)"] = $"₱{outstanding:N2}";

        switch (action)
        {
            case SpaceLifecycleAction.Resume:
            case SpaceLifecycleAction.Reassign:
            {
                var closedOn = a.ActiveTo!.Value;
                var earliest = FirstOfNextMonth(closedOn);
                facts["Earliest start"] = earliest.ToString("MMM d, yyyy");
                // The amount in force on the closure day: rates are append-only and never retroactive, so a later amount is not silently carried back.
                var rateAtClosure = ObligationRate.Resolve(space.Rates, closedOn);
                decimal? amount;
                if (action == SpaceLifecycleAction.Resume)
                {
                    amount = rateAtClosure?.Amount;
                    if (amount is null) return Block(NoApprovedAmount, "No approved monthly amount is recorded on this account, so none can be carried forward.");
                    if (space.HolderName is null) return Block(NoVerifiedHolder, "The holder of this account is not recorded, so the same holder cannot be verified.");
                    facts["Approved monthly amount"] = $"₱{amount:N2}";
                }
                else
                {
                    // Stated so the form can start from it; the new holder's amount is still the one the office enters and approves.
                    if (rateAtClosure is { } previous) facts["Previous approved amount"] = $"₱{previous.Amount:N2}";
                    if (string.IsNullOrWhiteSpace(c?.NewHolder) || c.NewHolder.Trim().Length > 200)
                        return Block(HolderRequired, "Name the new holder (up to 200 characters). A holder is never matched to an existing one by name.");
                    if (string.Equals(c.NewHolder.Trim(), space.HolderName?.Trim(), StringComparison.OrdinalIgnoreCase) && !c.ConfirmDistinctHolder)
                        return Block(SameHolderName, "The new holder has the same name as the current one. Use Resume for the same holder, or confirm that this is a different person.");
                    if (c.Amount is not { } approved || approved <= 0m || decimal.Round(approved, 2) != approved)
                        return Block(ApprovedAmountRequired, "Enter the approved monthly amount for the new holder.");
                    amount = approved;
                    if ((c.TermYears is not null || c.TermMonths is not null) && (!TermIsValid(c.TermYears, c.TermMonths) || c.Arrangement != OccupancyArrangement.SignedContract))
                        return Block(InvalidRequest, c.TermMonths is null ? "A contract term (1-50 years) is recorded only for a signed contract."
                            : "A contract term (1 month to 50 years) is recorded only for a signed contract.");
                    if (c.NewPayorId is { } payorId && payorId != Guid.Empty
                        && !await db.Payors.AsNoTracking().AnyAsync(x => x.MunicipalityId == actor.TenantId && x.Id == payorId, ct))
                        return Block(InvalidRequest, "Choose an existing Business Payor, or leave it blank.");
                    facts["New holder"] = c.NewHolder.Trim();
                    facts["Approved monthly amount"] = $"₱{amount:N2}";
                }
                if (c?.EffectiveDate is not { } start)
                    return new(null, null, version, facts, space, amount);
                if (start.Day != 1)
                    return Block(MidMonthStartPending, "Choose the first day of a month: a space is billed by the month, so the new occupancy starts on a month boundary.");
                if (start < earliest)
                    return Block(ClosureMonthOwnershipPending, $"The closure month ({closedOn:MMM yyyy}) stays with the previous holder. Choose {earliest:MMM d, yyyy} or later.");
                if (start < new DateOnly(2020, 1, 1) || start > Today.AddDays(366)) return Block(InvalidRequest, "Choose a realistic start date (not more than a year ahead).");
                facts["Start"] = start.ToString("MMM d, yyyy");
                if (action == SpaceLifecycleAction.Reassign && (c.TermYears is not null || c.TermMonths is not null))
                {
                    var signedTerm = DomainRules.NormalizeSpaceTerm(c.TermYears, c.TermMonths);
                    facts["Contract term"] = $"{start:MMM d, yyyy} to {DomainRules.TermLastDay(start, signedTerm.Years ?? 0, signedTerm.Months):MMM d, yyyy}";
                }
                return new(null, null, version, facts, space, amount, start);
            }
            case SpaceLifecycleAction.Renew:
            {
                var last = factsOf.TermLastDay!.Value;
                facts["Current term ends"] = last.ToString("MMM d, yyyy");
                if (c?.EffectiveDate is not { } commence) return new(null, null, version, facts, space, PriorTerm: space.LastTerm, PriorLastDay: last);
                if (commence <= last)
                    return Block(RenewalOverlapsTerm, $"The new term must begin after the current term ends ({last.AddDays(1):MMM d, yyyy} or later).");
                if (commence > Today.AddDays(366)) return Block(InvalidRequest, "Choose a realistic start date (not more than a year ahead).");
                if ((c.TermYears is null && c.TermMonths is null) || !TermIsValid(c.TermYears, c.TermMonths))
                    return Block(InvalidRequest, c.TermMonths is null ? "Enter the new term in whole years (1-50)." : "Enter the new term as years and/or months (1 month to 50 years).");
                var newTerm = DomainRules.NormalizeSpaceTerm(c.TermYears, c.TermMonths);
                facts["New term"] = $"{commence:MMM d, yyyy}, {DomainRules.DescribeTerm(newTerm.Years, newTerm.Months)}";
                return new(null, null, version, facts, space, Start: commence, PriorTerm: space.LastTerm, PriorLastDay: last);
            }
            case SpaceLifecycleAction.Retire:
            {
                // The one money rule: a balance still owed stays collectible and is never hidden by retiring the account.
                if (outstanding > 0m) return Block(OutstandingBalance, $"₱{outstanding:N2} is still owed on this account. Collect or settle it before retiring.");
                return new(null, null, version, facts, space);
            }
        }
        return Block(InvalidRequest, "Unknown lifecycle action.");
    }

    // ── preview ──
    public async Task<Result<LifecyclePreviewDto>> PreviewAsync(Guid accountId, LifecycleCommand command, CancellationToken ct = default)
    {
        var actor = Resolve("Admin", out var failure);
        if (actor is null) return Refuse<LifecyclePreviewDto>(failure!);
        if (!TryMap(command.Action, out var action)) return Result<LifecyclePreviewDto>.Failure("Only Resume, Renew, Reassign and Remove are space lifecycle actions.", ResultStatus.Invalid);
        var space = await LoadAsync(actor.TenantId, accountId, tracked: false, ct);
        if (space is null) return Result<LifecyclePreviewDto>.NotFound();
        var eval = await EvaluateAsync(actor, space, action, command, ct);
        // Without a start date a preview still states eligibility and the earliest start; it simply cannot be confirmed yet.
        var ready = eval.Code is null && NeedsStart(action) && eval.Start is null ? false : eval.Code is null;
        var code = eval.Code ?? (ready ? null : "StartRequired");
        var text = eval.Text ?? (ready ? null : "Choose the date it begins.");
        return Result<LifecyclePreviewDto>.Success(new(command.Action, ready, code, text, eval.Version, eval.Facts));
    }

    private static bool NeedsStart(SpaceLifecycleAction action) => action is SpaceLifecycleAction.Resume or SpaceLifecycleAction.Reassign or SpaceLifecycleAction.Renew;

    private static bool TryMap(LifecycleAction a, out SpaceLifecycleAction action)
    {
        action = a switch
        {
            LifecycleAction.Resume => SpaceLifecycleAction.Resume, LifecycleAction.Renew => SpaceLifecycleAction.Renew,
            LifecycleAction.Reassign => SpaceLifecycleAction.Reassign, LifecycleAction.Remove => SpaceLifecycleAction.Retire, _ => 0
        };
        return action != 0;
    }

    // ── execute ──
    public async Task<Result<LifecycleOutcome>> ExecuteAsync(Guid accountId, LifecycleExecution execution, CancellationToken ct = default)
    {
        var actor = Resolve("Admin", out var failure);
        if (actor is null) return Refuse<LifecycleOutcome>(failure!);
        var command = execution.Command;
        if (!TryMap(command.Action, out var action)) return Result<LifecycleOutcome>.Failure("Only Resume, Renew, Reassign and Remove are space lifecycle actions.", ResultStatus.Invalid);
        if (action == SpaceLifecycleAction.Retire && !LifecycleRules.HasRole(actor.Role, "SuperAdmin")) return Result<LifecycleOutcome>.Forbidden();
        if (execution.ClientOperationId == Guid.Empty || string.IsNullOrWhiteSpace(execution.ExpectedVersion))
            return Result<LifecycleOutcome>.Failure($"{InvalidRequest}: An operation id and the previewed version are required.", ResultStatus.Invalid);
        var reason = (command.Reason ?? string.Empty).Trim();
        if (reason.Length is 0 or > 500 || command.Evidence?.Length > 500)
            return Result<LifecycleOutcome>.Failure($"{InvalidRequest}: Give a reason of up to 500 characters.", ResultStatus.Invalid);

        var fingerprint = Fingerprint(accountId, command);
        await using var transaction = await db.BeginSpaceAccountTransactionAsync(actor.TenantId, ct);

        // Idempotency first: a retry of an operation this tenant already completed returns what it did, without re-evaluating a world it changed.
        var prior = await db.SpaceLifecycleEvents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.ClientOperationId == execution.ClientOperationId, ct);
        if (prior is not null)
            return prior.IntentFingerprint == fingerprint && prior.SourceAccountId == accountId
                ? Result<LifecycleOutcome>.Success(new(prior.Id, prior.SourceAccountId, prior.ResultAccountId, command.Action, Replayed: true))
                : Result<LifecycleOutcome>.Failure($"{OperationConflict}: That operation id was already used for a different request.", ResultStatus.Conflict);

        var space = await LoadAsync(actor.TenantId, accountId, tracked: true, ct);
        if (space is null) return Result<LifecycleOutcome>.NotFound();
        var eval = await EvaluateAsync(actor, space, action, command, ct);
        if (eval.Code is not null) return Result<LifecycleOutcome>.Failure($"{eval.Code}: {eval.Text}", eval.Code == InvalidRequest ? ResultStatus.Invalid : ResultStatus.Conflict);
        if (NeedsStart(action) && eval.Start is null) return Result<LifecycleOutcome>.Failure($"{InvalidRequest}: Choose the date it begins.", ResultStatus.Invalid);
        if (!string.Equals(eval.Version, execution.ExpectedVersion, StringComparison.Ordinal))
            return Result<LifecycleOutcome>.Failure($"{StalePreview}: This account or its space changed since it was previewed. Preview it again.", ResultStatus.Conflict);

        var now = DateTime.UtcNow;
        var account = space.Account;
        var registration = space.Registration!;
        Guid? resultId = null;
        Guid eventId = Guid.NewGuid();
        SpaceLifecycleEvent evt;
        try
        {
            switch (action)
            {
                case SpaceLifecycleAction.Resume:
                case SpaceLifecycleAction.Reassign:
                {
                    var start = eval.Start!.Value;
                    var holder = action == SpaceLifecycleAction.Resume ? account.ActualOccupant : command.NewHolder!.Trim();
                    var payor = action == SpaceLifecycleAction.Resume ? account.PayorId : command.NewPayorId is { } p && p != Guid.Empty ? p : null;
                    var successor = ObligationAccount.Create(actor.TenantId, account.Kind, payor ?? Guid.Empty, null, registration.DisplayNumber, null, null, start,
                        actor.Username, actualOccupant: holder);
                    // Same-holder resume carries the occupancy basis; a new holder states their own. The physical space's measured area stays with the space.
                    var arrangement = action == SpaceLifecycleAction.Resume ? account.Arrangement : command.Arrangement;
                    var reference = action == SpaceLifecycleAction.Resume ? account.ContractReference : command.Reference;
                    if (arrangement is not null) successor.SetOccupancyBasis(arrangement, arrangement == OccupancyArrangement.SpaceOnly ? null : reference);
                    // A new holder's signed contract is recorded with its own term, as the registration form records one: effective from the start date.
                    var newSigned = action == SpaceLifecycleAction.Reassign && arrangement == OccupancyArrangement.SignedContract;
                    if (arrangement == OccupancyArrangement.SignedContract || account.AreaSqm is not null || account.AreaLocation is not null)
                        successor.SetLeaseDetails(action == SpaceLifecycleAction.Resume ? account.ContractName : command.ContractName,
                            newSigned ? start : null, newSigned ? command.TermYears : null, account.AreaSqm, account.AreaLocation,
                            action == SpaceLifecycleAction.Resume && arrangement == OccupancyArrangement.SignedContract ? account.ContractMonthlyRate : null,
                            newSigned ? command.TermMonths : null);
                    successor.FollowOccupancy(account);
                    var rate = ObligationRate.Create(actor.TenantId, successor.Id, start, eval.Amount!.Value, actor.Username);
                    db.ObligationAccounts.Add(successor);
                    db.ObligationRates.Add(rate);
                    resultId = successor.Id;
                    evt = SpaceLifecycleEvent.Record(actor.TenantId, registration.Id, action, account.Id, successor.Id, start, execution.ClientOperationId,
                        fingerprint, eval.Version, reason, command.Evidence, actor.UserId, actor.Username, now);
                    break;
                }
                case SpaceLifecycleAction.Renew:
                {
                    var commence = eval.Start!.Value;
                    evt = SpaceLifecycleEvent.Record(actor.TenantId, registration.Id, action, account.Id, null, commence, execution.ClientOperationId,
                        fingerprint, eval.Version, reason, command.Evidence, actor.UserId, actor.Username, now);
                    // Appended evidence only: no rate, assessment, period or Collection is touched, and the account's own descriptive term is left as recorded.
                    db.SpaceContractTerms.Add(SpaceContractTerm.Create(actor.TenantId, account.Id, registration.Id, eval.PriorTerm?.Id, commence, command.TermYears ?? 0,
                        command.ContractName, command.Reference, command.Evidence ?? reason, evt.Id, actor.UserId, actor.Username, now, command.TermMonths));
                    break;
                }
                default:
                {
                    account.Retire(now);
                    evt = SpaceLifecycleEvent.Record(actor.TenantId, registration.Id, action, account.Id, null, Today, execution.ClientOperationId,
                        fingerprint, eval.Version, reason, command.Evidence, actor.UserId, actor.Username, now);
                    break;
                }
            }
            // The event is added first so the term's FK to it is satisfied within the same unit of work.
            db.SpaceLifecycleEvents.Add(evt);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (ArgumentException ex) { db.ChangeTracker.Clear(); return Result<LifecycleOutcome>.Failure($"{InvalidRequest}: {ex.Message}", ResultStatus.Invalid); }
        catch (InvalidOperationException ex) { db.ChangeTracker.Clear(); return Result<LifecycleOutcome>.Failure($"{InvalidRequest}: {ex.Message}", ResultStatus.Conflict); }
        catch (DbUpdateException)
        {
            // The database is the last guard: an overlapping occupancy, a second successor or a reused operation id lost a race with another request.
            db.ChangeTracker.Clear();
            return Result<LifecycleOutcome>.Failure("ConcurrentChange: Another change to this space was saved first. Preview it again.", ResultStatus.Conflict);
        }
        return Result<LifecycleOutcome>.Success(new(evt.Id, account.Id, resultId, command.Action, Replayed: false));
    }
}
