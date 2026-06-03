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
}
