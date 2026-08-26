using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// One linked stall's outstanding position for the payor's own portal. <see cref="OutstandingBalance"/> is the sum of what
/// the stall currently owes, which for a market stall is its unpaid days and for every other facility its unpaid months.
/// </summary>
/// <param name="IsDailyBilled">
/// Whether this space is charged by the DAY. The distinction has to reach the payor's screen: a market stall was shown a
/// monthly rate it is never billed, while what it actually owes is a day's fee for each day it has traded.
/// </param>
/// <param name="DailyRate">The office's own fee for one day at this stall. Zero where the space is not billed daily.</param>
/// <param name="DaysOwed">
/// Days this stall still owes in the current month: elapsed, within its term, not closed, and neither collected nor
/// excused. Zero where the space is not billed daily.
/// </param>
public sealed record PayorStallBalanceDto(
    Guid StallId,
    string StallNo,
    FacilityCode Facility,
    string Occupant,
    decimal MonthlyRate,
    decimal OutstandingBalance,
    int UnpaidMonths,
    string? OldestUnpaidPeriod,
    bool IsDailyBilled = false,
    decimal DailyRate = 0m,
    int DaysOwed = 0);
