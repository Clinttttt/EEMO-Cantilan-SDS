namespace EEMOCantilanSDS.Application.Dtos.DailyCollections;

/// <summary>What recording one amount against an NPM stall's payable days would do — without recording anything.</summary>
/// <param name="Outcome">Exact, NotWholeInstallments, ExceedsPayable, NothingPayable or Invalid.</param>
/// <param name="ExactDates">The oldest-first days the amount settles in full. Empty unless the amount is exact.</param>
/// <param name="NearestBelow">The largest amount below the one entered that settles whole days, or null.</param>
/// <param name="NearestAbove">The smallest amount above the one entered that settles whole days, or null.</param>
/// <param name="PayableTotal">What the month's payable days come to in all.</param>
/// <param name="Unallocated">The part of the entered amount no payable day takes. Never recorded as a paid day.</param>
public sealed record NpmAmountQuoteDto(
    decimal Amount, string Outcome, IReadOnlyList<DateOnly> ExactDates, decimal Allocated, decimal Unallocated,
    decimal? NearestBelow, decimal? NearestAbove, decimal PayableTotal, int PayableDays);
