using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of FacilityReportsRepository: date-range calculation helpers.
public partial class FacilityReportsRepository
{
    #region Date Range Calculation Helpers

    /// <summary>
    /// Calculates the start and end dates for the current period based on the report period type.
    /// </summary>
    /// <param name="period">The report period (Weekly, Monthly, Yearly)</param>
    /// <param name="year">The year</param>
    /// <param name="month">The month (required for Weekly and Monthly)</param>
    /// <param name="weekNumber">The week number (required for Weekly, 1-5)</param>
    /// <returns>Tuple of (startDate, endDate) as DateOnly</returns>
    private static (DateOnly startDate, DateOnly endDate) CalculateDateRange(
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber)
    {
        return period switch
        {
            ReportPeriod.Weekly => CalculateWeeklyDateRange(year, month!.Value, weekNumber!.Value),
            ReportPeriod.Monthly => CalculateMonthlyDateRange(year, month!.Value),
            ReportPeriod.Yearly => CalculateYearlyDateRange(year),
            _ => throw new ArgumentException($"Invalid report period: {period}")
        };
    }

    /// <summary>
    /// Calculates the start and end dates for the previous period (for growth comparison).
    /// </summary>
    /// <param name="period">The report period (Weekly, Monthly, Yearly)</param>
    /// <param name="year">The year</param>
    /// <param name="month">The month (required for Weekly and Monthly)</param>
    /// <param name="weekNumber">The week number (required for Weekly, 1-5)</param>
    /// <returns>Tuple of (startDate, endDate) as DateOnly</returns>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousPeriodDateRange(
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber)
    {
        return period switch
        {
            ReportPeriod.Weekly => CalculatePreviousWeekDateRange(year, month!.Value, weekNumber!.Value),
            ReportPeriod.Monthly => CalculatePreviousMonthDateRange(year, month!.Value),
            ReportPeriod.Yearly => CalculatePreviousYearDateRange(year),
            _ => throw new ArgumentException($"Invalid report period: {period}")
        };
    }

    /// <summary>
    /// Calculates the date range for a specific week in a month.
    /// Week 1 = Days 1-7, Week 2 = Days 8-14, Week 3 = Days 15-21, Week 4 = Days 22-28, Week 5 = Days 29-31
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculateWeeklyDateRange(
        int year,
        int month,
        int weekNumber)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var startDay = (weekNumber - 1) * 7 + 1;
        var endDay = Math.Min(weekNumber * 7, daysInMonth);

        // A week beyond the month's days (e.g. week 5 of a 28-day February) has no days.
        // Return an empty range (start > end) so callers iterate nothing instead of throwing.
        if (startDay > daysInMonth)
        {
            var firstOfMonth = new DateOnly(year, month, 1);
            return (firstOfMonth, firstOfMonth.AddDays(-1));
        }

        var startDate = new DateOnly(year, month, startDay);
        var endDate = new DateOnly(year, month, endDay);

        return (startDate, endDate);
    }

    /// <summary>
    /// Calculates the date range for a specific month.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculateMonthlyDateRange(
        int year,
        int month)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        return (startDate, endDate);
    }

    /// <summary>
    /// Calculates the date range for a specific year.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculateYearlyDateRange(int year)
    {
        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        return (startDate, endDate);
    }

    /// <summary>
    /// Calculates the date range for the previous week.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousWeekDateRange(
        int year,
        int month,
        int weekNumber)
    {
        // If week 1, go to previous month's last week
        if (weekNumber == 1)
        {
            var previousMonth = month == 1 ? 12 : month - 1;
            var previousYear = month == 1 ? year - 1 : year;
            var daysInPreviousMonth = DateTime.DaysInMonth(previousYear, previousMonth);
            
            // Calculate the last week of previous month
            var lastWeekNumber = (daysInPreviousMonth + 6) / 7; // Ceiling division
            return CalculateWeeklyDateRange(previousYear, previousMonth, lastWeekNumber);
        }

        // Otherwise, just go to previous week in same month
        return CalculateWeeklyDateRange(year, month, weekNumber - 1);
    }

    /// <summary>
    /// Calculates the date range for the previous month.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousMonthDateRange(
        int year,
        int month)
    {
        var previousMonth = month == 1 ? 12 : month - 1;
        var previousYear = month == 1 ? year - 1 : year;

        return CalculateMonthlyDateRange(previousYear, previousMonth);
    }

    /// <summary>
    /// Calculates the date range for the previous year.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousYearDateRange(int year)
    {
        return CalculateYearlyDateRange(year - 1);
    }

    #endregion

}
