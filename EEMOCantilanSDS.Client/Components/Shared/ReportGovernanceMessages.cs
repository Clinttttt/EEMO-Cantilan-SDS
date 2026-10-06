namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>
/// The server answers report-governance conflicts with stable machine codes; this is the one place they become office wording.
/// Anything else is shown as a short generic message rather than echoing server text.
/// </summary>
public static class ReportGovernanceMessages
{
    public static string For(string? code) => code switch
    {
        "ReportIntentConflict" => "This change was already saved with different details. Reopen it and try again.",
        "ReportRevisionChanged" => "Someone else changed this report line. Reopen it to see the latest, then try again.",
        "SystemAmountChanged" => "The system amount changed while you were editing. Reopen it to see the current figure.",
        "InvalidReportAmount" => "Enter an amount in pesos with up to two decimals.",
        _ => "The change could not be saved."
    };
}
