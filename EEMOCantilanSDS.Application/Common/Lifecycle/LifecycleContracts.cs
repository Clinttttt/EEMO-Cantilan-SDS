using EEMOCantilanSDS.Domain.Common;
namespace EEMOCantilanSDS.Application.Common.Lifecycle;

/// <summary>
/// Which source owns the account. The lifecycle is shared; the financial model never is: a permanent stall (Stall + Contract terms),
/// a monthly Space Rental (one ObligationAccount interval) and a Fiesta/Araw event lot (one event period) keep their own rules.
/// </summary>
public enum LifecycleSource
{
    Stall = 1,
    SpaceRental = 2,
    EventLot = 3
}

public enum LifecycleAction
{
    View = 1,
    Close = 2,
    Resume = 3,
    Renew = 4,
    Reassign = 5,
    Remove = 6
}

public enum LifecycleState
{
    Active = 1,
    Closed = 2,
    /// <summary>The recorded term has run out while the account is still open (a stall's lapsed contract).</summary>
    TermExpired = 3,
    /// <summary>A closed, fully settled occupancy the office has retired. Nothing is deleted.</summary>
    Retired = 4,
    /// <summary>An end date is recorded but has not arrived: the account is still active and is billed to that date.</summary>
    Ending = 5,
    /// <summary>Closed, and someone now holds the same space.</summary>
    Superseded = 6
}

/// <summary>Stable, machine-readable reasons an action is blocked. The UI shows <see cref="AllowedAction.Reason"/>; code branches on these.</summary>
public static class LifecycleReason
{
    public const string RoleRequired = "RoleRequired";
    public const string AlreadyClosed = "AlreadyClosed";
    public const string NotClosed = "NotClosed";
    public const string AccountClosed = "AccountClosed";
    public const string StillActive = "StillActive";
    public const string SectionClosed = "SectionClosed";
    public const string MonthEvidenceActivated = "MonthEvidenceActivated";
    public const string NotReLet = "NotReLet";
    public const string TermStillRunning = "TermStillRunning";
    /// <summary>The stall rules that still have no source workflow (a stall has none left; kept for the read view of unsupported sources).</summary>
    public const string PendingApproval = "PendingApproval";
    public const string NotApplicableToEventLot = "NotApplicableToEventLot";
}

/// <summary>
/// One lifecycle action and whether it may be taken now.
/// </summary>
/// <param name="RequiredRole">The lowest role the existing server path accepts ("Admin" means Admin or SuperAdmin).</param>
/// <param name="Enforced">
/// True when the existing command/endpoint itself refuses the request while it is blocked (role, state and evidence guards that live on
/// the server). False when the block is advisory: the current command would still run, so the screen must not present the action,
/// but nothing here claims the server would stop a direct call. This is reported honestly rather than assumed.
/// </param>
public sealed record AllowedAction(LifecycleAction Action, bool Allowed, string? ReasonCode, string? Reason, string RequiredRole, bool Enforced);

public sealed record LifecycleHistoryEntry(DateOnly Date, string Kind, string Description);

/// <summary>The read-only lifecycle of one account: identity, state, history and the actions available. No balances and no ledger data.</summary>
public sealed record AccountLifecycleDto(
    LifecycleSource Source,
    Guid AccountId,
    /// <summary>The space/stall number or label, as the office names it.</summary>
    string Label,
    /// <summary>The facility code or obligation kind that owns the account, e.g. "NPM" or "KanmanggaySpaceRental".</summary>
    string SourceDetail,
    string? Holder,
    LifecycleState State,
    DateOnly? ActiveFrom,
    DateOnly? EndedOn,
    DateOnly? TermExpiry,
    IReadOnlyList<AllowedAction> Actions,
    IReadOnlyList<LifecycleHistoryEntry> History);

/// <summary>One source's read-only lifecycle view. Registered per source; the query dispatches on <see cref="Source"/>.</summary>
public interface IAccountLifecycle
{
    /// <summary>The sources this adapter reads. A single adapter may serve related sources (a monthly space and an event lot).</summary>
    bool Handles(LifecycleSource source);

    /// <summary>The account's lifecycle, or null when it does not exist in the caller's municipality (never another tenant's).</summary>
    Task<AccountLifecycleDto?> GetAsync(LifecycleSource source, Guid accountId, string? actorRole, CancellationToken ct);
}

/// <summary>Picks the adapter that handles a source. Registered once (the composition root forbids registering one service type twice).</summary>
public sealed class AccountLifecycleResolver(IEnumerable<IAccountLifecycle> adapters, IEnumerable<IAccountLifecycleExecutor>? executors = null)
{
    public IAccountLifecycle? For(LifecycleSource source) => adapters.FirstOrDefault(a => a.Handles(source));
    public IAccountLifecycleExecutor? ExecutorFor(LifecycleSource source) => executors?.FirstOrDefault(e => e.Handles(source));
}

// ── Execution contracts (Phase 3). Preview/confirm with a state version, a durable client operation id and an intent fingerprint. ──

/// <summary>What the actor wants done. Only the fields the action needs are read; the server validates every one.</summary>
public sealed record LifecycleCommand(
    LifecycleAction Action,
    /// <summary>Resume/Reassign: when the new occupancy begins. Renew: when the new term commences. Retire: ignored.</summary>
    DateOnly? EffectiveDate,
    string Reason,
    string? Evidence = null,
    /// <summary>Reassign: the new holder, named explicitly. Never matched to an existing holder by name.</summary>
    string? NewHolder = null,
    /// <summary>Reassign: an explicit Business Payor link for the new holder, if the office has one.</summary>
    Guid? NewPayorId = null,
    /// <summary>Reassign: the office confirms the new holder is a different person although the name is the same.</summary>
    bool ConfirmDistinctHolder = false,
    /// <summary>Reassign: the approved monthly amount for the new occupancy (required). Resume resolves it from the closed account and ignores this.</summary>
    decimal? Amount = null,
    int? TermYears = null,
    string? ContractName = null,
    string? Reference = null,
    EEMOCantilanSDS.Domain.Enums.OccupancyArrangement? Arrangement = null);

/// <summary>The outcome of a preview: whether the command could be executed now, why not, and the state version to confirm.</summary>
public sealed record LifecyclePreviewDto(
    LifecycleAction Action,
    bool CanExecute,
    string? ReasonCode,
    string? Explanation,
    /// <summary>Opaque. The actor confirms it on execute; any change to the account or its space in between makes the execute a conflict.</summary>
    string Version,
    IReadOnlyDictionary<string, string> Facts);

public sealed record LifecycleExecution(Guid ClientOperationId, string ExpectedVersion, LifecycleCommand Command);

public sealed record LifecycleOutcome(Guid EventId, Guid AccountId, Guid? ResultAccountId, LifecycleAction Action, bool Replayed);

/// <summary>A source's lifecycle EXECUTION. Only sources with a safe, replayable, concurrency-protected implementation provide one.</summary>
public interface IAccountLifecycleExecutor
{
    bool Handles(LifecycleSource source);
    Task<Result<LifecyclePreviewDto>> PreviewAsync(LifecycleSource source, Guid accountId, LifecycleCommand command, CancellationToken ct);
    Task<Result<LifecycleOutcome>> ExecuteAsync(LifecycleSource source, Guid accountId, LifecycleExecution execution, CancellationToken ct);
}
