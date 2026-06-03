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
}
