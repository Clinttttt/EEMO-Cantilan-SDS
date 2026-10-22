using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Lifecycle;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Words for the shared lifecycle screens. It names things; it decides nothing: eligibility, reasons and dates are the server's, and a
/// reason this file does not know is shown exactly as the server stated it.
/// </summary>
public static class LifecyclePresentation
{
    /// <summary>The four actions a monthly space account offers, in display order.</summary>
    public static readonly LifecycleAction[] SpaceActions = [LifecycleAction.Resume, LifecycleAction.Renew, LifecycleAction.Reassign, LifecycleAction.Remove];

    public static string Label(LifecycleAction action) => action switch
    {
        LifecycleAction.Resume => "Resume account",
        LifecycleAction.Renew => "Renew contract",
        LifecycleAction.Reassign => "Reassign holder",
        LifecycleAction.Remove => "Retire account",
        LifecycleAction.Close => "Close account",
        _ => action.ToString()
    };

    /// <summary>A stall's wording: its Reassign is placing the payor in a NEW stall, which is not a handover of the same space.</summary>
    public static string Label(LifecycleAction action, LifecycleSource source) => source == LifecycleSource.Stall
        ? action switch { LifecycleAction.Renew => "Renew term", LifecycleAction.Reassign => "Assign new stall", LifecycleAction.Remove => "Remove account", _ => Label(action) }
        : Label(action);

    /// <summary>The query-string name of an action, for deep links from other pages.</summary>
    public static LifecycleAction? FromQuery(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "resume" => LifecycleAction.Resume,
        "renew" => LifecycleAction.Renew,
        "reassign" => LifecycleAction.Reassign,
        "retire" or "remove" => LifecycleAction.Remove,
        _ => null
    };

    public static string QueryName(LifecycleAction action) => action == LifecycleAction.Remove ? "retire" : action.ToString().ToLowerInvariant();

    public static string StateLabel(LifecycleState state) => state switch
    {
        LifecycleState.Active => "Active",
        LifecycleState.Closed => "Closed",
        LifecycleState.TermExpired => "Term ended",
        LifecycleState.Retired => "Retired",
        LifecycleState.Ending => "Ending",
        LifecycleState.Superseded => "Replaced",
        _ => state.ToString()
    };

    public static string StateTone(LifecycleState state) => state switch
    {
        LifecycleState.Active => "active",
        LifecycleState.TermExpired => "partial",
        _ => "inactive"
    };

    /// <summary>A failure split into the server's code (when it sent one) and its message.</summary>
    public sealed record Failure(string? Code, string Message, bool Stale = false, bool CanRetrySameIntent = false, bool SessionEnded = false);

    public static Failure Describe<T>(Result<T> result)
    {
        switch (result.Status)
        {
            case ResultStatus.Unauthorized: return new(null, "Your session has ended. Sign in again, then repeat the action.", SessionEnded: true);
            case ResultStatus.Forbidden: return new(LifecycleReason.RoleRequired, "Your account is not allowed to do this.");
            case ResultStatus.NotFound: return new(null, "This account was not found in your municipality. It may have been changed or removed.");
        }
        var error = result.Error ?? string.Empty;
        var colon = error.IndexOf(':');
        var hasCode = colon > 0 && colon < 40 && error[..colon].All(char.IsLetter);
        var code = hasCode ? error[..colon] : null;
        var message = (hasCode ? error[(colon + 1)..] : error).Trim();
        if (string.IsNullOrWhiteSpace(message)) message = "The server could not complete this request.";
        return new(code, message, Stale: code == SpaceLifecycleWorkflow.StalePreview, CanRetrySameIntent: code == "ConcurrentChange");
    }

    /// <summary>The field a server reason belongs beside, so the message sits where the person fixes it.</summary>
    public static string? FieldOf(string? code) => code switch
    {
        SpaceLifecycleWorkflow.MidMonthStartPending or SpaceLifecycleWorkflow.ClosureMonthOwnershipPending or LifecycleReasonCodes.StartRequired
            or SpaceLifecycleWorkflow.RenewalOverlapsTerm => "date",
        SpaceLifecycleWorkflow.HolderRequired or SpaceLifecycleWorkflow.SameHolderName => "holder",
        SpaceLifecycleWorkflow.ApprovedAmountRequired => "amount",
        _ => null
    };
}

internal static class LifecycleReasonCodes
{
    public const string StartRequired = "StartRequired";
}
