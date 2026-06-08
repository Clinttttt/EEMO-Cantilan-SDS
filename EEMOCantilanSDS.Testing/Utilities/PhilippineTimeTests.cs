using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

public class PhilippineTimeTests
{
    [Fact]
    public void Now_IsUtcPlusEightHours()
    {
        var delta = PhilippineTime.Now - DateTime.UtcNow;
        Assert.True(Math.Abs((delta - TimeSpan.FromHours(8)).TotalSeconds) < 5);
    }

    [Fact]
    public void TodayUtcRange_SpansExactlyOneUtcDay_AndContainsNow()
    {
        var (startUtc, endUtc) = PhilippineTime.TodayUtcRange();

        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);          // Npgsql timestamptz requires Utc kind
        Assert.Equal(TimeSpan.FromDays(1), endUtc - startUtc);
        Assert.InRange(DateTime.UtcNow, startUtc, endUtc);      // current instant is within "today (PH)"
    }

    [Fact]
    public void ToPhilippineTime_AddsEightHours_AsUnspecifiedKind()
    {
        // 2026-01-31 19:00 UTC is 2026-02-01 03:00 in Manila.
        var utc = new DateTime(2026, 1, 31, 19, 0, 0, DateTimeKind.Utc);

        var local = PhilippineTime.ToPhilippineTime(utc);

        Assert.Equal(new DateTime(2026, 2, 1, 3, 0, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Fact]
    public void MonthUtcRange_CoversTheLocalMonthInUtcInstants()
    {
        var (startUtc, endUtc) = PhilippineTime.MonthUtcRange(2026, 2);

        // PHT Feb 1 00:00 == UTC Jan 31 16:00; PHT Mar 1 00:00 == UTC Feb 28 16:00.
        Assert.Equal(new DateTime(2026, 1, 31, 16, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 2, 28, 16, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);
    }

    [Fact]
    public void MonthUtcRange_AttributesBoundaryInstantToTheLocalMonth()
    {
        // A trip at 2026-01-31 19:00 UTC is locally Feb 1 → must fall in February's range, not January's.
        var trip = new DateTime(2026, 1, 31, 19, 0, 0, DateTimeKind.Utc);

        var (febStart, febEnd) = PhilippineTime.MonthUtcRange(2026, 2);
        var (janStart, janEnd) = PhilippineTime.MonthUtcRange(2026, 1);

        Assert.True(trip >= febStart && trip < febEnd, "boundary trip should be in February (PHT)");
        Assert.False(trip >= janStart && trip < janEnd, "boundary trip should not be in January (PHT)");
    }
}
