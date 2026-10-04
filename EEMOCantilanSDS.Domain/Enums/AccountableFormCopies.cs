namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// The copies of ONE accountable receipt set (a single serial). They are copy-level exception details only: one serial is one
/// accountable receipt, never three independent receipts.
/// </summary>
[Flags]
public enum AccountableFormCopies
{
    Original = 1,
    Duplicate = 2,
    Triplicate = 4,
    WholeSet = Original | Duplicate | Triplicate
}

/// <summary>What an external follow-up reference evidences.</summary>
public enum AccountableFormReferenceKind
{
    /// <summary>The Report of Collections and Deposits (or equivalent) that carried the cancelled copies.</summary>
    Cancellation = 1,

    /// <summary>The notice of loss, report or other external evidence for a lost form or copy.</summary>
    Loss = 2
}
