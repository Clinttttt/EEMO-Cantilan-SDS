using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Lifecycle;

/// <summary>What the stall rules need to know. Plain values, so eligibility is testable without a database.</summary>
public sealed record StallLifecycleFacts(
    bool Closed,
    bool ContractExpired,
    bool ReLetToSomeoneElse,
    bool SectionClosed,
    bool MonthEvidenceActivated);

/// <summary>
/// What the monthly-space rules need. <c>Ended</c> is a recorded end date that has arrived (a future end date is only "ending": the account is still active and
/// billed to it). <c>TermLastDay</c> is the last day of the latest recorded term, from a renewal or the contract itself.
/// </summary>
public sealed record SpaceLifecycleFacts(
    ObligationKind Kind, bool HasRegistration, bool Ended, DateOnly? EndsOn, bool Retired, bool HasSuccessor, bool SignedContract, DateOnly? TermLastDay, DateOnly Today);

/// <summary>
/// Lifecycle eligibility, stated once and server-side. Every rule below mirrors what the existing source paths actually do; where the
/// existing command does not enforce a guard, the action is still reported blocked (so no screen offers it) but flagged
/// <see cref="AllowedAction.Enforced"/> = false. Nothing here changes who may do what: roles are those of the current controllers.
///
/// <para>Audited permissions (2026-10-20): StallsController is Admin or SuperAdmin for Close/Resume (PATCH status), Renew,
/// AssignPastOccupant and the reads; SoftDelete is SuperAdmin only. ObligationsController is SuperAdmin for account Close
/// (and create/holder/rate/import); reads are Admin or SuperAdmin. No handler adds a further role check.</para>
/// </summary>
public static class LifecycleRules
{
    private const string Admin = "Admin";
    private const string SuperAdmin = "SuperAdmin";

    public static bool HasRole(string? actorRole, string required) =>
        required == Admin ? actorRole is Admin or SuperAdmin : actorRole == SuperAdmin;

    public static IReadOnlyList<AllowedAction> ForStall(StallLifecycleFacts f, string? role)
    {
        AllowedAction Allow(LifecycleAction a, string r) => new(a, true, null, null, r, true);
        AllowedAction Block(LifecycleAction a, string r, string code, string why, bool enforced) => new(a, false, code, why, r, enforced);
        AllowedAction RoleBlock(LifecycleAction a, string r) =>
            Block(a, r, LifecycleReason.RoleRequired, r == SuperAdmin ? "Only the Head may do this." : "An Admin or the Head may do this.", true);

        // Close — existing path refuses only when month evidence is activated; it does not refuse an already closed stall.
        AllowedAction close = !HasRole(role, Admin) ? RoleBlock(LifecycleAction.Close, Admin)
            : f.Closed ? Block(LifecycleAction.Close, Admin, LifecycleReason.AlreadyClosed, "This account is already closed.", false)
            : f.MonthEvidenceActivated ? Block(LifecycleAction.Close, Admin, LifecycleReason.MonthEvidenceActivated,
                "Month evidence is activated. Review it through month-source reconciliation first.", true)
            : Allow(LifecycleAction.Close, Admin);

        // Resume — refuses a closed market section and activated month evidence; it does not refuse a stall that is not closed.
        AllowedAction resume = !HasRole(role, Admin) ? RoleBlock(LifecycleAction.Resume, Admin)
            : !f.Closed ? Block(LifecycleAction.Resume, Admin, LifecycleReason.NotClosed, "Only a closed account can be resumed.", false)
            : f.SectionClosed ? Block(LifecycleAction.Resume, Admin, LifecycleReason.SectionClosed,
                "The market section is closed. Reopen the section to bring its stalls back.", true)
            : f.MonthEvidenceActivated ? Block(LifecycleAction.Resume, Admin, LifecycleReason.MonthEvidenceActivated,
                "Month evidence is activated. Review it through month-source reconciliation first.", true)
            : Allow(LifecycleAction.Resume, Admin);

        // Renew — intended eligibility is a term that has run out on an account that is still open: that is what Closed Accounts offers
        // (Expired / Lapsed rows) and the only caller of the endpoint. The command itself has NO state guard: it would accept a renewal
        // of a running term (an early renewal, or a handover when a different occupant is named) and of a closed stall. Those are reported
        // blocked so no screen advertises them, with Enforced = false because a direct call would still run.
        AllowedAction renew = !HasRole(role, Admin) ? RoleBlock(LifecycleAction.Renew, Admin)
            : f.Closed ? Block(LifecycleAction.Renew, Admin, LifecycleReason.AccountClosed, "A closed account is resumed, not renewed.", false)
            : !f.ContractExpired ? Block(LifecycleAction.Renew, Admin, LifecycleReason.TermStillRunning, "The current term has not run out.", false)
            : Allow(LifecycleAction.Renew, Admin) with { Enforced = false };

        // Reassign — "place a returning payor in a stall of their own", offered only for a past occupancy of a stall somebody else now
        // holds (the register's StallReLet). The command reads the past occupancy and has no such state guard.
        AllowedAction reassign = !HasRole(role, Admin) ? RoleBlock(LifecycleAction.Reassign, Admin)
            : !f.ReLetToSomeoneElse ? Block(LifecycleAction.Reassign, Admin, LifecycleReason.NotReLet,
                "Only a past occupancy of a stall now held by someone else can be placed in a stall of its own.", false)
            : Allow(LifecycleAction.Reassign, Admin) with { Enforced = false };

        // Remove — Head only; the command refuses a stall that is neither closed nor past its term. Soft delete: history is kept.
        AllowedAction remove = !HasRole(role, SuperAdmin) ? RoleBlock(LifecycleAction.Remove, SuperAdmin)
            : !f.Closed && !f.ContractExpired ? Block(LifecycleAction.Remove, SuperAdmin, LifecycleReason.StillActive,
                "Only closed or expired accounts can be removed.", true)
            : Allow(LifecycleAction.Remove, SuperAdmin);

        return [new(LifecycleAction.View, HasRole(role, Admin), HasRole(role, Admin) ? null : LifecycleReason.RoleRequired, null, Admin, true),
            close, resume, renew, reassign, remove];
    }

    /// <summary>
    /// A monthly Space Rental (Kanmanggay, Vegetable/Fruit), in the same shape and wording as a stall: Admin or the Head may Resume, Renew and Reassign,
    /// only the Head may Close or Remove. Resume is for an ended account nobody has taken over; Renew is for an open signed-contract account whose recorded
    /// term has run out; Reassign puts a different holder in the same space; Remove retires a closed account. There is no per-LGU switch: what blocks an
    /// action is the account's own state, the actor's role, and the money (a balance still owed blocks Remove at preview). A Fiesta/Araw event lot is an
    /// event, never a monthly lease. Date and amount rules (a start on the first of a month, an approved amount) are checked at preview and execute.
    /// </summary>
    public static IReadOnlyList<AllowedAction> ForSpace(SpaceLifecycleFacts f, string? role)
    {
        AllowedAction Allow(LifecycleAction a, string r) => new(a, true, null, null, r, true);
        AllowedAction Block(LifecycleAction a, string r, string code, string why) => new(a, false, code, why, r, true);
        AllowedAction RoleBlock(LifecycleAction a, string r) =>
            Block(a, r, LifecycleReason.RoleRequired, r == SuperAdmin ? "Only the Head may do this." : "An Admin or the Head may do this.");
        var view = new AllowedAction(LifecycleAction.View, HasRole(role, Admin), HasRole(role, Admin) ? null : LifecycleReason.RoleRequired, null, Admin, true);

        if (f.Kind == ObligationKind.FiestaArawLotRental)
        {
            AllowedAction Lot(LifecycleAction a) => Block(a, SuperAdmin, LifecycleReason.NotApplicableToEventLot, "An event lot is a one-time event, not a recurring lease.");
            return [view, Lot(LifecycleAction.Close), Lot(LifecycleAction.Resume), Lot(LifecycleAction.Renew), Lot(LifecycleAction.Reassign), Lot(LifecycleAction.Remove)];
        }

        string? Common(LifecycleAction a, out string text)
        {
            text = string.Empty;
            if (f.Retired) { text = "This account has been retired."; return "Retired"; }
            if (!f.HasRegistration) { text = "This account needs its one-time space setup first."; return "NoSpaceRegistration"; }
            return null;
        }
        string NotEnded() => f.EndsOn is { } ends ? $"This account is still active; it ends on {ends:MMM d, yyyy}." : "Only a closed account can do this.";

        AllowedAction Close = !HasRole(role, SuperAdmin) ? RoleBlock(LifecycleAction.Close, SuperAdmin)
            : f.Retired ? Block(LifecycleAction.Close, SuperAdmin, "Retired", "This account has been retired.")
            : f.Ended ? Block(LifecycleAction.Close, SuperAdmin, LifecycleReason.AlreadyClosed, "This account is already closed.")
            : Allow(LifecycleAction.Close, SuperAdmin);

        AllowedAction Gate(LifecycleAction a, string needs, Func<AllowedAction?> state)
        {
            if (!HasRole(role, needs)) return RoleBlock(a, needs);
            if (Common(a, out var why) is { } code) return Block(a, needs, code, why);
            return state() ?? Allow(a, needs);
        }

        // Resume (stall: "put this lessee back in the same space"): an ended account nobody has taken over. The closed period is not billed.
        var resume = Gate(LifecycleAction.Resume, Admin, () =>
            !f.Ended ? Block(LifecycleAction.Resume, Admin, LifecycleReason.NotClosed, NotEnded())
            : f.HasSuccessor ? Block(LifecycleAction.Resume, Admin, "SuccessorExists", "Someone already holds this space, so this account cannot be resumed.")
            : null);

        // Renew (stall: a lapsed term on an account that is still open): needs a signed contract with a recorded term that has run out.
        var renew = Gate(LifecycleAction.Renew, Admin, () =>
            f.Ended ? Block(LifecycleAction.Renew, Admin, LifecycleReason.AccountClosed, "A closed account is resumed, not renewed.")
            : !f.SignedContract ? Block(LifecycleAction.Renew, Admin, "NoSignedContract", "There is no signed contract term to renew.")
            : f.TermLastDay is null ? Block(LifecycleAction.Renew, Admin, "NoRecordedTerm", "No contract term is recorded for this account.")
            : f.TermLastDay >= f.Today ? Block(LifecycleAction.Renew, Admin, LifecycleReason.TermStillRunning, "The current term has not run out.")
            : null);

        // Reassign (a space is not moved: a different holder takes THIS space, as a successor occupancy of the ended account).
        var reassign = Gate(LifecycleAction.Reassign, Admin, () =>
            !f.Ended ? Block(LifecycleAction.Reassign, Admin, LifecycleReason.NotClosed, NotEnded())
            : f.HasSuccessor ? Block(LifecycleAction.Reassign, Admin, "SuccessorExists", "Someone already holds this space.")
            : null);

        // Remove (stall: Head only, closed accounts, history kept): retiring never deletes; a balance still owed is checked at preview.
        var remove = Gate(LifecycleAction.Remove, SuperAdmin, () =>
            !f.Ended ? Block(LifecycleAction.Remove, SuperAdmin, LifecycleReason.StillActive, "Only closed accounts can be removed.") : null);

        return [view, Close, resume, renew, reassign, remove];
    }
}
