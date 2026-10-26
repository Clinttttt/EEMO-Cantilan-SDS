using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Which billing month a Collector's monthly payment is for. No month stated means this month (the monthly operation's own screen); an earlier month is
/// how a delinquent month is collected. A month that has not begun, or that is not a real month, is refused: a collector never records rent ahead of time.
/// </summary>
public static class MobileBillingMonth
{
    public static Result<(int Year, int Month)> Resolve(int? year, int? month, DateOnly today)
    {
        if (year is null && month is null) return Result<(int, int)>.Success((today.Year, today.Month));
        if (year is not { } y || month is not { } m || y < 2000 || m is < 1 or > 12)
            return Result<(int, int)>.Failure("Choose the billing month to collect.", ResultStatus.Invalid);
        return new DateOnly(y, m, 1) > new DateOnly(today.Year, today.Month, 1)
            ? Result<(int, int)>.Failure("A month that has not begun cannot be collected yet.", ResultStatus.Invalid)
            : Result<(int, int)>.Success((y, m));
    }
}
