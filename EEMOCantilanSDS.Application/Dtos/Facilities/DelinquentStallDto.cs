using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// A stall that is behind on payments across the whole of its present account — from the day the occupancy in
/// force began, up to the last month that has closed (the month in progress is never "unpaid", since it can
/// still be paid). <see cref="MonthsUnpaid"/> counts those unsettled months (3+ = delinquent, 1–2 = arrears) and
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
    string Section = ""
);
