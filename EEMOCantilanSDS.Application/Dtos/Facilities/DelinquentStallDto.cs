using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// A stall that is behind on payments across the whole of its present account — from the day the occupancy in
/// force began, up to the last month that has closed (the month in progress is never "unpaid", since it can
/// still be paid). <see cref="MonthsUnpaid"/> counts those unsettled months; one or more fully elapsed unpaid months
/// meet the confirmed Delinquent definition. Arrears qualification is not derived from this age count.
/// <see cref="OutstandingBalance"/> is what they add up to, so this figure is the same debt the Closed /
/// Inactive register and the whole-time Follow-up History state for the same account.
/// <para>
/// <see cref="TermLapsed"/> marks an account whose contract term has run out while the space was never handed
/// over — the tenant is ordinarily still trading and the office is still collecting, which is why the account is
/// on this list at all and not treated as finished.
/// </para>
/// Shared by the dashboard and the Financial Reports attention list so both agree.
/// </summary>
public record DelinquentStallDto(
    FacilityCode FacilityCode,
    string StallNo,
    string Occupant,
    int MonthsUnpaid,
    decimal OutstandingBalance,
    Guid? StallId = null,
    bool TermLapsed = false,
    string Section = "",
    // The account split by obligation (IA-019 / IA-020 / IA-069), set when the source classified it (Classified = true). MonthsUnpaid and
    // OutstandingBalance stay the WHOLE account for every existing reader; these partition the unpaid elapsed months with no month in both:
    // Arrears = previous-calendar-year months, plus every month of an expired contract; Delinquent = current-year months of a live account.
    bool Classified = false,
    int ArrearsMonths = 0,
    decimal ArrearsAmount = 0m,
    int DelinquentMonths = 0,
    decimal DelinquentAmount = 0m,
    // The oldest and newest billing month inside each category, so a row can name the periods it is about (yyyy-MM).
    string? ArrearsFirstPeriod = null,
    string? ArrearsLastPeriod = null,
    string? DelinquentFirstPeriod = null,
    string? DelinquentLastPeriod = null
)
{
    /// <summary>Months counted as Delinquent: the classified count, or the whole age where a producer did not classify (every reader before IA-069).</summary>
    public int DelinquentMonthCount => Classified ? DelinquentMonths : MonthsUnpaid;

    /// <summary>Money counted as Delinquent, on the same rule as <see cref="DelinquentMonthCount"/>.</summary>
    public decimal DelinquentOutstanding => Classified ? DelinquentAmount : OutstandingBalance;

    /// <summary>Money counted as Arrears; zero where nothing was classified.</summary>
    public decimal ArrearsOutstanding => Classified ? ArrearsAmount : 0m;
}
