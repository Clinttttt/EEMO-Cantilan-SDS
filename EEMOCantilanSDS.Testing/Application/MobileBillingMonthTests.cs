using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.UnitTest.Application;

public class MobileBillingMonthTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void No_month_stated_is_this_month_and_an_earlier_month_is_how_a_delinquent_month_is_collected()
    {
        Assert.Equal((2026, 10), MobileBillingMonth.Resolve(null, null, Today).Value);
        Assert.Equal((2026, 8), MobileBillingMonth.Resolve(2026, 8, Today).Value);
        Assert.Equal((2025, 12), MobileBillingMonth.Resolve(2025, 12, Today).Value);
        Assert.Equal((2026, 10), MobileBillingMonth.Resolve(2026, 10, Today).Value);               // this month by name is fine
    }

    [Theory]
    [InlineData(2026, 11)]      // not begun: never rent ahead of time
    [InlineData(2027, 1)]
    [InlineData(2026, 13)]      // not a month
    [InlineData(2026, 0)]
    [InlineData(1999, 5)]
    [InlineData(2026, null)]    // half a month
    [InlineData(null, 8)]
    public void A_month_that_has_not_begun_or_is_not_real_is_refused(int? year, int? month)
    {
        var result = MobileBillingMonth.Resolve(year, month, Today);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}
