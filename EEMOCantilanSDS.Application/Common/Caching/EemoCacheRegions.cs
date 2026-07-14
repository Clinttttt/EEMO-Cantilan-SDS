using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Caching;

public static class EemoCacheRegions
{
    public static string Period(string tenantCode, int year, int month)
        => $"{EemoCacheKeys.NormalizeTenant(tenantCode)}:period:{year:0000}:{month:00}";

    public static string Dashboard(string tenantCode, int year, int month)
        => $"{EemoCacheKeys.NormalizeTenant(tenantCode)}:dashboard:{year:0000}:{month:00}";

    public static string Reports(string tenantCode, int year, int month)
        => $"{EemoCacheKeys.NormalizeTenant(tenantCode)}:reports:{year:0000}:{month:00}";

    public static string FacilityPeriod(string tenantCode, FacilityCode facilityCode, int year, int month)
        => $"{EemoCacheKeys.NormalizeTenant(tenantCode)}:facility:{facilityCode.ToString().ToLowerInvariant()}:{year:0000}:{month:00}";

    public static string ReferenceData(string tenantCode)
        => $"{EemoCacheKeys.NormalizeTenant(tenantCode)}:reference";

    public static string ActivityFeed(string tenantCode)
        => $"{EemoCacheKeys.NormalizeTenant(tenantCode)}:activity-feed";

    public static IReadOnlyCollection<string> DashboardOverviewRegions(string tenantCode, int year, int month)
        => new[]
        {
            Period(tenantCode, year, month),
            Dashboard(tenantCode, year, month),
            ActivityFeed(tenantCode),
            ReferenceData(tenantCode)
        };

    public static IReadOnlyCollection<string> FacilitySummariesRegions(string tenantCode, int year, int month)
        => new[]
        {
            Period(tenantCode, year, month),
            Dashboard(tenantCode, year, month),
            ReferenceData(tenantCode)
        };

    public static IReadOnlyCollection<string> FinancialReportRegions(
        string tenantCode,
        ReportPeriod period,
        int year,
        int? month,
        FacilityCode? facilityCode)
    {
        var regions = new List<string> { ReferenceData(tenantCode), ActivityFeed(tenantCode) };
        if (period == ReportPeriod.Yearly && month is null)
        {
            for (var m = 1; m <= 12; m++)
                AddPeriodReportRegions(regions, tenantCode, year, m, facilityCode);
        }
        else if (month is int m)
        {
            AddPeriodReportRegions(regions, tenantCode, year, m, facilityCode);
        }
        else
        {
            regions.Add($"{EemoCacheKeys.NormalizeTenant(tenantCode)}:reports:{year:0000}:all");
        }

        return regions.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyCollection<string> MonthEndReportRegions(string tenantCode, int year, int month)
        => new[]
        {
            Period(tenantCode, year, month),
            Reports(tenantCode, year, month),
            ReferenceData(tenantCode)
        };

    public static IReadOnlyCollection<string> FacilityReportRegions(
        string tenantCode,
        FacilityCode facilityCode,
        int year,
        int? month)
    {
        var regions = new List<string> { Reports(tenantCode, year, month ?? 1), ReferenceData(tenantCode) };
        if (month is int m)
        {
            regions.Add(Period(tenantCode, year, m));
            regions.Add(FacilityPeriod(tenantCode, facilityCode, year, m));
        }
        else
        {
            // Yearly/weekly-without-month: invalidate on any month of the year for this facility.
            for (var mm = 1; mm <= 12; mm++)
            {
                regions.Add(Period(tenantCode, year, mm));
                regions.Add(FacilityPeriod(tenantCode, facilityCode, year, mm));
            }
        }
        return regions.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyCollection<string> FollowUpHistoryRegions(string tenantCode, int year, int month)
        => FollowUpHistoryRegions(tenantCode, year, month, false);

    public static IReadOnlyCollection<string> FollowUpHistoryRegions(string tenantCode, int year, int month, bool wholeYear)
    {
        if (!wholeYear)
            return new[]
            {
                Period(tenantCode, year, month),
                Reports(tenantCode, year, month),
                ReferenceData(tenantCode)
            };

        // Whole-year missing-OR aggregates every month → invalidate the snapshot when ANY month changes.
        var regions = new List<string> { ReferenceData(tenantCode) };
        for (var m = 1; m <= 12; m++)
        {
            regions.Add(Period(tenantCode, year, m));
            regions.Add(Reports(tenantCode, year, m));
        }
        return regions.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyCollection<string> StallHolderListRegions(string tenantCode)
        => new[] { ReferenceData(tenantCode) };

    public static IReadOnlyCollection<string> ClosedAccountsRegions(string tenantCode)
        => new[] { ReferenceData(tenantCode), ActivityFeed(tenantCode) };

    private static void AddPeriodReportRegions(
        List<string> regions,
        string tenantCode,
        int year,
        int month,
        FacilityCode? facilityCode)
    {
        regions.Add(Period(tenantCode, year, month));
        regions.Add(Reports(tenantCode, year, month));
        if (facilityCode is FacilityCode facility)
            regions.Add(FacilityPeriod(tenantCode, facility, year, month));
    }
}
