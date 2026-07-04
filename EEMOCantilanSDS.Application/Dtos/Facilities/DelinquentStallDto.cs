using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// A stall that is behind on payments over the rolling 12-month window (excluding the current,
/// in-progress month). <see cref="MonthsUnpaid"/> is the count of unpaid/partial billing months in the
/// window (3+ = delinquent, 1–2 = arrears) and <see cref="OutstandingBalance"/> is their cumulative
/// balance due. Shared by the dashboard and the Financial Reports attention list so both agree.
/// </summary>
public record DelinquentStallDto(
    FacilityCode FacilityCode,
    string StallNo,
    string Occupant,
    int MonthsUnpaid,
    decimal OutstandingBalance,
    Guid? StallId = null
);
