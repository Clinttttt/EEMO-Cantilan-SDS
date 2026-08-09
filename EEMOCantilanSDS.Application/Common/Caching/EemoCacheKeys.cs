using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Caching;

public static class EemoCacheKeys
{
    public static string DashboardOverview(string tenantCode, int year, int month)
        => $"{NormalizeTenant(tenantCode)}:dashboard:overview:{year:0000}:{month:00}";

    public static string FacilitySummaries(string tenantCode, int year, int month)
        => $"{NormalizeTenant(tenantCode)}:facilities:sidebar:{year:0000}:{month:00}";

    public static string FinancialReport(
        string tenantCode,
        ReportPeriod period,
        int year,
        int? month,
        FacilityCode? facilityCode)
    {
        var facility = facilityCode?.ToString().ToLowerInvariant() ?? "all";
        var monthSegment = month is int m ? m.ToString("00") : "all";
        return $"{NormalizeTenant(tenantCode)}:reports:financial:{period.ToString().ToLowerInvariant()}:{year:0000}:{monthSegment}:{facility}";
    }

    public static string MonthEndReport(string tenantCode, int year, int month)
        => $"{NormalizeTenant(tenantCode)}:reports:month-end:{year:0000}:{month:00}";

    public static string FacilityReport(
        string tenantCode,
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber)
    {
        var monthSegment = month is int m ? m.ToString("00") : "all";
        var weekSegment = weekNumber is int w ? w.ToString("00") : "all";
        return $"{NormalizeTenant(tenantCode)}:reports:facility:{facilityCode.ToString().ToLowerInvariant()}:{period.ToString().ToLowerInvariant()}:{year:0000}:{monthSegment}:{weekSegment}";
    }

    public static string FollowUpHistory(string tenantCode, int year, int month)
        => FollowUpHistory(tenantCode, year, month, false);

    public static string FollowUpHistory(string tenantCode, int year, int month, bool wholeYear)
        => wholeYear
            ? $"{NormalizeTenant(tenantCode)}:reports:follow-up-history:{year:0000}:whole-year"
            : $"{NormalizeTenant(tenantCode)}:reports:follow-up-history:{year:0000}:{month:00}";

    /// <summary>
    /// The cumulative "Whole time" view: outstanding accounts with their whole balances.
    ///
    /// <para>Keyed on the anchor date despite being cumulative, because the value depends on it: the view now also
    /// carries the month in progress, and its delinquency figures are assessed as of that anchor. The key described
    /// itself as belonging to no year or month, which stopped being true the moment the current month was included -
    /// two people looking at "Whole time" with different years selected shared one entry, and whichever asked first
    /// decided what the other saw.</para>
    /// </summary>
    public static string FollowUpHistoryAllTime(string tenantCode, int year, int month)
        => $"{NormalizeTenant(tenantCode)}:reports:follow-up-history:all-time:{year:0000}:{month:00}";

    public static string StallHolderList(
        string tenantCode,
        FacilityCode facilityCode,
        MarketSection? section,
        string? searchTerm)
    {
        var sectionSegment = section?.ToString().ToLowerInvariant() ?? "all";
        // The search term comes from a query string, so it is unbounded in both length and variety. It reaches a
        // per-key semaphore map that is deliberately never pruned, so an arbitrary term would let a caller grow
        // that map without limit - the cache entries themselves are size-capped, the semaphores are not. Hashing
        // keeps one entry per distinct search, as before, while bounding what a key can be.
        var searchSegment = string.IsNullOrWhiteSpace(searchTerm)
            ? "all"
            : StableHash(searchTerm.Trim().ToLowerInvariant());
        return $"{NormalizeTenant(tenantCode)}:stalls:holders:{facilityCode.ToString().ToLowerInvariant()}:{sectionSegment}:{searchSegment}";
    }

    public static string ClosedAccounts(string tenantCode)
        => $"{NormalizeTenant(tenantCode)}:stalls:closed-accounts";

    public static string ClosedAccounts(string tenantCode, DateOnly asOf)
        => $"{NormalizeTenant(tenantCode)}:stalls:closed-accounts:{asOf:yyyy-MM-dd}";

    internal static string NormalizeTenant(string tenantCode)
        => string.IsNullOrWhiteSpace(tenantCode)
            ? "default"
            : tenantCode.Trim().ToLowerInvariant();

    /// <summary>
    /// A short, fixed-length, stable rendering of caller-supplied text for use inside a cache key. Stable across
    /// processes and restarts, unlike string.GetHashCode, so the same search does not land on a different key after
    /// a deployment and quietly halve the cache's usefulness. Not a security hash - only a bound on key length.
    /// </summary>
    private static string StableHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
