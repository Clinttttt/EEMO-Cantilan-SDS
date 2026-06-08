namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// Single source of truth for "now" in the system's operating timezone
/// (Philippines, fixed UTC+8 — the country observes no daylight saving).
/// Use for business-day logic (today, current month, contract expiry, streaks, trip-day).
/// Persisted timestamps (CreatedAt/UpdatedAt/PaidAt, token expiry, lockout) stay in UTC.
/// </summary>
public static class PhilippineTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    // Kind=Unspecified (a local wall-clock value, not an instant): if it is ever persisted to a
    // PostgreSQL "timestamp with time zone" column, Npgsql throws instead of silently shifting by +8.
    public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.Add(Offset), DateTimeKind.Unspecified);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>
    /// UTC instant range [StartUtc, EndUtc) covering the current Philippine calendar day —
    /// for filtering UTC-stored timestamps by "today" in local terms.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) TodayUtcRange()
    {
        var startUtc = DateTime.SpecifyKind(Today.ToDateTime(TimeOnly.MinValue).Add(-Offset), DateTimeKind.Utc);
        return (startUtc, startUtc.AddDays(1));
    }

    /// <summary>
    /// Converts a UTC-stored instant to the equivalent Philippine wall-clock value
    /// (Kind=Unspecified, never re-shifted). Use for displaying/attributing stored timestamps locally.
    /// </summary>
    public static DateTime ToPhilippineTime(DateTime utc)
        => DateTime.SpecifyKind(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified).Add(Offset), DateTimeKind.Unspecified);

    /// <summary>
    /// UTC instant range [StartUtc, EndUtc) covering a Philippine calendar month —
    /// for filtering/aggregating UTC-stored timestamps by a local (PHT) month.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) MonthUtcRange(int year, int month)
    {
        var startLocal = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var startUtc = DateTime.SpecifyKind(startLocal.Add(-Offset), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(startLocal.AddMonths(1).Add(-Offset), DateTimeKind.Utc);
        return (startUtc, endUtc);
    }

    /// <summary>
    /// UTC instant range [StartUtc, EndUtc) covering a single Philippine calendar day —
    /// for filtering UTC-stored timestamps by a local (PHT) date.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayUtcRange(DateOnly date)
    {
        var startUtc = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).Add(-Offset), DateTimeKind.Utc);
        return (startUtc, startUtc.AddDays(1));
    }
}
