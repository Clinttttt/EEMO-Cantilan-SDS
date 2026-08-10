using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// One month of a market vendor's collection history as the office's own records state it: which space, which month,
/// and how many market days were paid for.
///
/// <para>
/// The market is not billed by the month, so its history cannot be a monthly payment. It is collected per market day
/// at a fixed daily fee, and what the office's books record for a vendor is a count of days against a receipt - not a
/// rent figure. This row is that: the days, not an amount.
/// </para>
///
/// <para>
/// WHICH days is not something the books state, and inventing dates would be a lie dressed as precision. The import
/// fills the month's collectable days in order, earliest first, skipping the days nobody owes for - before the term
/// began, after it ended, a facility-wide closure, a day already collected, and a day already excused. The count is
/// therefore honoured exactly while the dates stay defensible, and the screen says so plainly before the office
/// commits to it.
/// </para>
///
/// <para>The amount is DERIVED from the facility's own daily fee for each day settled rather than typed. An LGU may
/// have changed its fee mid-year, so a typed total would disagree with the rate on record for those very days, and
/// storing it would create a second figure that arrears could not be reconciled against.</para>
/// </summary>
/// <param name="RowNumber">The row on the office's sheet, so an error names a line they can find.</param>
/// <param name="StallNo">The space, matched within the facility AND section - the market has three spaces called "1".</param>
/// <param name="Occupant">Who the office's sheet says held it, for the clerk to verify against the account.</param>
/// <param name="BillingYear">The year the days fall in.</param>
/// <param name="BillingMonth">The month the days fall in.</param>
/// <param name="DaysPaid">How many market days the vendor paid for in that month.</param>
/// <summary>
/// One market day as the office's own collection sheet records it: the date, and the receipt it was collected under.
///
/// <para>The receipt is per DAY, not per month, because that is how the market is actually collected: a run of days
/// may sit under one OR, or each day may carry its own, and a sheet that records several cannot be reduced to one
/// without discarding receipts the office would later be asked to produce. Left blank, the row's own OR applies.</para>
/// </summary>
public record ImportDailyDay(DateOnly Date, string? OrNumber = null);

/// <param name="OrNumber">The Official Receipt the days were collected under, where the sheet records one for the
/// month. A day that names its own receipt overrides it.</param>
/// <param name="Days">
/// The exact days, when the office knows them.
///
/// <para>Supplied, they are honoured exactly and nothing is filled in around them: the office has stated which days
/// it collected, and topping the row up to the claimed count with a day of the system's choosing would invent a
/// collection. A day that cannot be settled is reported rather than substituted.</para>
///
/// <para>Empty — a sheet that records only a count — falls back to filling the month's collectable days in order,
/// earliest first, all under the row's own receipt.</para>
/// </param>
public record ImportDailyPaymentRow(
    int RowNumber,
    string StallNo,
    string? Occupant,
    int BillingYear,
    int BillingMonth,
    int DaysPaid,
    string? OrNumber,
    IReadOnlyList<ImportDailyDay>? Days = null);

/// <summary>Why a row of market history was not recorded, or how it was.</summary>
public enum ImportDailyOutcome
{
    /// <summary>Every day the row claimed was settled.</summary>
    RecordedInFull = 0,

    /// <summary>
    /// Some days were settled and the rest could not be. The month had fewer collectable days left than the row
    /// claimed - already collected, excused, closed, or outside the term - and the reason says which.
    /// </summary>
    RecordedInPart = 1,

    /// <summary>Nothing was left to settle: the month's days were already collected or excused.</summary>
    AlreadyRecorded = 2,

    /// <summary>The row could not be recorded; the error says why.</summary>
    Rejected = 3,
}

/// <summary>The outcome for one row of imported market history.</summary>
public record BulkImportDailyRowResult(
    int RowNumber,
    string StallNo,
    string Period,
    int DaysClaimed,
    int DaysSettled,
    decimal AmountRecorded,
    ImportDailyOutcome Outcome,
    string? Error);

/// <summary>What an imported market history did, row by row.</summary>
public record BulkImportDailyResultDto(
    int TotalRows,
    int RecordedCount,
    int PartialCount,
    int AlreadyRecordedCount,
    int RejectedCount,
    int TotalDaysSettled,
    decimal TotalRecorded,
    IReadOnlyList<BulkImportDailyRowResult> Results);
