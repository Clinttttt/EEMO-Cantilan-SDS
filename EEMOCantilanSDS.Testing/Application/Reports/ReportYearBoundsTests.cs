using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The report validators cap the year a caller may ask for at "next year". Read from a static clock, that bound moved on its
/// own and could not be stated in a test — a case written in December would have accepted a year that the same case rejects in
/// January. With the clock injected, the boundary is asserted from both sides on a fixed date.
///
/// <para>Next year is allowed deliberately: the office prepares the coming year's figures before it starts.</para>
/// </summary>
public class ReportYearBoundsTests
{
    /// <summary>
    /// Deliberately NOT the current year. Pinned first to the real one, nine of these ten cases passed with the handler
    /// ignoring the injected clock — the static bound happened to agree with the fixed one, so the cases proved nothing. A
    /// date the server's calendar cannot coincide with is what makes each case load-bearing.
    /// </summary>
    private static readonly DateTime LateIn2031 = new(2031, 12, 31, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(2031, true)]    // the year the caller is standing in
    [InlineData(2032, true)]    // next year — allowed on purpose
    [InlineData(2033, false)]   // the year after next is not a report the office can have
    [InlineData(2001, true)]    // just inside the lower bound
    [InlineData(2000, false)]   // the lower bound itself is excluded
    public void FacilityHistoryAcceptsUpToNextYear(int year, bool expected)
    {
        var validator = new GetFacilityHistoryQueryValidator(new FixedClock(LateIn2031));

        var result = validator.Validate(new GetFacilityHistoryQuery(FacilityCode.NPM, year));

        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public void TheBoundFollowsTheClock_NotTheCalendarOnTheServer()
    {
        // The same year is accepted by a validator standing in 2031 and refused by one standing in 2024. That difference is
        // the whole point of injecting the clock: the rule is now a function of a stated date.
        var query = new GetFacilityHistoryQuery(FacilityCode.NPM, 2032);

        Assert.True(new GetFacilityHistoryQueryValidator(new FixedClock(LateIn2031)).Validate(query).IsValid);
        Assert.False(new GetFacilityHistoryQueryValidator(
            new FixedClock(new DateTime(2024, 6, 1, 8, 0, 0, DateTimeKind.Utc))).Validate(query).IsValid);
    }

    [Theory]
    [InlineData(2032, true)]
    [InlineData(2033, false)]
    public void FacilityReportsUsesTheSameBound(int year, bool expected)
    {
        var validator = new GetFacilityReportsQueryValidator(new FixedClock(LateIn2031));

        var result = validator.Validate(
            new GetFacilityReportsQuery(FacilityCode.NPM, ReportPeriod.Yearly, year, null, null));

        Assert.Equal(expected, result.IsValid);
    }

    [Theory]
    [InlineData(2032, true)]
    [InlineData(2033, false)]
    public void TheFinancialReportUsesTheSameBound(int year, bool expected)
    {
        var validator = new GetFinancialReportQueryValidator(new FixedClock(LateIn2031));

        var result = validator.Validate(new GetFinancialReportQuery(ReportPeriod.Yearly, year, null, null));

        Assert.Equal(expected, result.IsValid);
    }
}
