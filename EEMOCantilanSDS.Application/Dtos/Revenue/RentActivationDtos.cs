using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>Review one facility's monthly rent for one billing month: which fresh, unpaid rows could be made collectible with an SRC. Writes nothing.</summary>
public sealed record RentActivationPlanRequest(
    FacilityCode Facility, int Year, int Month, SettlementCutoverReconciliationEvidence? Evidence = null);

/// <summary>One stall's row in the plan. <see cref="Eligible"/> rows are fresh (unpaid, no legacy settlement evidence); every other row is named with its reason and stays exactly as it is.</summary>
public sealed record RentActivationRowDto(
    Guid StallId, string StallNo, string Occupant, Guid? PaymentRecordId, SettlementAuthority? Authority,
    bool Eligible, string? Reason, IReadOnlyList<string> Blockers);

public sealed record RentActivationPlanDto(
    FacilityCode Facility, int Year, int Month, IReadOnlyList<RentActivationRowDto> Rows,
    int EligibleCount, int ExcludedCount, IReadOnlyList<string> FacilityBlockers, bool Ready, string PlanFingerprint,
    IReadOnlyList<Guid>? AffectedCollectorIds = null);

/// <summary>The Head's act: make the reviewed fresh rows of one facility-month canonical. Bound to the reviewed plan, so anything that changed since is refused.</summary>
public sealed record RentActivationRequest(
    FacilityCode Facility, int Year, int Month, string ExpectedPlanFingerprint, SettlementCutoverReconciliationEvidence Evidence);

public sealed record RentActivationResultDto(
    FacilityCode Facility, int Year, int Month, int Activated, int Failed, IReadOnlyList<RentActivationRowDto> Rows, string? Message);
