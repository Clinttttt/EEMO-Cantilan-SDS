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

    public static string FollowUpHistory(string tenantCode, int year, int month)
        => $"{NormalizeTenant(tenantCode)}:reports:follow-up-history:{year:0000}:{month:00}";

    public static string StallHolderList(
        string tenantCode,
        FacilityCode facilityCode,
        MarketSection? section,
        string? searchTerm)
    {
        var sectionSegment = section?.ToString().ToLowerInvariant() ?? "all";
        var searchSegment = string.IsNullOrWhiteSpace(searchTerm) ? "all" : Uri.EscapeDataString(searchTerm.Trim().ToLowerInvariant());
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
}
