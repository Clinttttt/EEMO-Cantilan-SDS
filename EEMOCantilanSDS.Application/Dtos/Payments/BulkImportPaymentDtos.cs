using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// One month's payment as the office's own records state it: which space, which billing month, how much was
/// received, and the receipt it was issued under.
///
/// <para>
/// This is how an office already keeping paper books adopts the system without waiting three years for its
/// contracts to run out. Importing the history rather than an opening balance keeps arrears DERIVED - the system
/// goes on working out what is owed from the term and the payments against it, exactly as it does for a payment
/// taken today - so nothing new has to be trusted and no figure exists that cannot be traced to a receipt.
/// </para>
/// </summary>
/// <param name="RowNumber">The row on the office's sheet, so an error names a line they can find.</param>
/// <param name="StallNo">The space, matched within the facility and section.</param>
/// <param name="Occupant">Who the office's sheet says held it, for the clerk to verify against the account.</param>
/// <param name="BillingYear">The year the payment was FOR, not the year it was received.</param>
/// <param name="BillingMonth">The month the payment was FOR.</param>
/// <param name="AmountPaid">
/// What was actually received. Carried as a figure rather than a tick because a month paid in part must stay
/// outstanding for the remainder - a short payment recorded as settled is how a debt disappears.
/// </param>
/// <param name="OrNumber">The Official Receipt. Every real payment has one, and the arrears lists treat a
/// payment without one as needing follow-up, so history imported without it would raise a false alarm per row.</param>
/// <param name="DatePaid">When it was received, where the office's records say.</param>
public record ImportPaymentRow(
    int RowNumber,
    string StallNo,
    string? Occupant,
    int BillingYear,
    int BillingMonth,
    decimal AmountPaid,
    string? OrNumber,
    DateTime? DatePaid);

/// <summary>Why a row was not recorded, or how it was.</summary>
public enum ImportPaymentOutcome
{
    /// <summary>Recorded in full: the amount met the month's rent.</summary>
    RecordedPaid = 0,

    /// <summary>Recorded as a part payment: the month stays outstanding for the remainder.</summary>
    RecordedPartial = 1,

    /// <summary>A payment for this space and month is already on record, so nothing was written.</summary>
    AlreadyRecorded = 2,

    /// <summary>The row could not be recorded; <see cref="BulkImportPaymentRowResult.Error"/> says why.</summary>
    Rejected = 3,
}

/// <summary>The outcome for one row of an imported payment history.</summary>
public record BulkImportPaymentRowResult(
    int RowNumber,
    string StallNo,
    string Period,
    decimal AmountPaid,
    ImportPaymentOutcome Outcome,
    string? Error);

/// <summary>What an imported payment history did, row by row.</summary>
public record BulkImportPaymentResultDto(
    int TotalRows,
    int RecordedCount,
    int PartialCount,
    int AlreadyRecordedCount,
    int RejectedCount,
    decimal TotalRecorded,
    IReadOnlyList<BulkImportPaymentRowResult> Results);
