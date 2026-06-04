using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public class GetFacilityReportsQueryValidatorTests
{
    private readonly GetFacilityReportsQueryValidator _validator = new();

    private static GetFacilityReportsQuery Weekly(int year, int month, int week) =>
        new(FacilityCode.NPM, ReportPeriod.Weekly, year, month, week);

    [Fact]
    public void Monthly_WithValidMonth_IsValid()
    {
        var result = _validator.Validate(
            new GetFacilityReportsQuery(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 2, null));

        Assert.True(result.IsValid);
    }

    // Regression: Week 5 of a 28-day February previously reached CalculateWeeklyDateRange
    // and threw ArgumentOutOfRangeException (new DateOnly(2026, 2, 29)) -> HTTP 500.
    [Fact]
    public void Weekly_Week5_OfTwentyEightDayFebruary_IsRejected()
    {
        var result = _validator.Validate(Weekly(2026, 2, 5)); // Feb 2026 = 28 days = 4 weeks

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("does not exist"));
    }

    [Theory]
    [InlineData(2026, 1, 5, true)]   // January (31 days) -> 5 weeks
    [InlineData(2026, 4, 5, true)]   // April (30 days)   -> 5 weeks
    [InlineData(2024, 2, 5, true)]   // February leap (29 days) -> 5 weeks
    [InlineData(2026, 2, 5, false)]  // February (28 days) -> only 4 weeks
    [InlineData(2026, 2, 4, true)]   // week 4 of a 28-day February still exists
    public void Weekly_WeekExistenceMatchesMonthLength(int year, int month, int week, bool expectedValid)
    {
        var result = _validator.Validate(Weekly(year, month, week));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Weekly_WithoutMonth_IsRejected()
    {
        var result = _validator.Validate(
            new GetFacilityReportsQuery(FacilityCode.NPM, ReportPeriod.Weekly, 2026, null, 1));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Weekly_WeekNumberOutOfBounds_IsRejected(int week)
    {
        var result = _validator.Validate(Weekly(2026, 1, week));

        Assert.False(result.IsValid);
    }
}
