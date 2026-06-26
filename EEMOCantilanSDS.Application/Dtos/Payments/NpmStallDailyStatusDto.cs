namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// Per-stall daily-collection status for the NPM operational table (current month, as of today).
/// NPM is collected daily (₱30/day), so the operationally useful signal is "did they pay today",
/// how many days this month were collected, and when the last collection happened.
/// </summary>
public record NpmStallDailyStatusDto(
    Guid StallId,
    bool PaidToday,
    int DaysPaidThisMonth,
    DateOnly? LastPaidDate,
    // OR number of the most recent paid daily collection this month (NPM ORs live on the daily
    // collections, not a monthly record). Used to show a reference OR on the collection receipt.
    string? LastORNumber = null,
    // True when today's daily record marks the payor excused/absent (₱0 owed for the day).
    bool AbsentToday = false
);
