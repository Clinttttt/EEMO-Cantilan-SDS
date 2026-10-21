namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>Deployment-supplied approval evidence, never accepted from a payment or registration form.</summary>
public sealed record NpmOpeningMonthPolicy(Guid MunicipalityId, Guid FacilityId, DateOnly EffectiveFrom,
    Guid ApprovedByUserId, DateTime ApprovedAtUtc, string ApprovalReference,
    bool PartPaymentsApproved = false);

/// <summary>Empty by default. Installing code cannot opt an existing municipality into new billing.</summary>
public sealed record NpmOpeningMonthPolicies(IReadOnlyList<NpmOpeningMonthPolicy>? Approvals = null)
{
    internal NpmOpeningMonthPolicy Require(Guid tenant, Guid facility, DateOnly opening, DateTime occupancyRecordedAt, DateTime now)
    {
        var matches = (Approvals ?? []).Where(x => x.MunicipalityId == tenant && x.FacilityId == facility
            && x.EffectiveFrom <= opening).OrderByDescending(x => x.EffectiveFrom).ToArray();
        if (matches.Length == 0 || matches.Count(x => x.EffectiveFrom == matches[0].EffectiveFrom) != 1)
            throw new InvalidOperationException("OFFICE_REVIEW_REQUIRED: Exactly one tenant/facility opening-month approval must apply.");
        var policy = matches[0];
        if (tenant == Guid.Empty || facility == Guid.Empty || policy.EffectiveFrom == default
            || policy.ApprovedByUserId == Guid.Empty || policy.ApprovedAtUtc.Kind != DateTimeKind.Utc
            || policy.ApprovedAtUtc > now || policy.ApprovedAtUtc > occupancyRecordedAt
            || string.IsNullOrWhiteSpace(policy.ApprovalReference) || policy.ApprovalReference.Length > 1000)
            throw new InvalidOperationException("OFFICE_REVIEW_REQUIRED: Prospective approval must precede occupancy registration and identify its authority.");
        return policy;
    }
}

/// <summary>Frozen source/rate provenance; descriptive registration amounts are retained for comparison only.</summary>
public sealed record NpmOpeningMonthRateEvidence(string AssessmentRule, Guid MunicipalityId, Guid FacilityId,
    Guid StallId, Guid ContractId, DateOnly OccupancyStart, DateOnly RateAsOf, Guid MonthlyRateId,
    DateOnly MonthlyRateEffectiveFrom, decimal ApprovedMonthlyAmount, decimal ApprovedDailyInstallment,
    decimal RecordedStallMonthlyAmount, decimal RecordedContractMonthlyAmount, string RateRecordedBy,
    DateTime RateRecordedAtUtc, Guid PolicyApprovedByUserId, DateTime PolicyApprovedAtUtc,
    DateOnly PolicyEffectiveFrom, string ApprovalReference, bool DailyInstallmentsApproved,
    bool PartPaymentsApproved);

public sealed record NpmOpeningMonthPreparationRequest(Guid StallId, Guid ContractId, DateOnly PeriodStart);
public sealed record NpmOpeningMonthAssessmentReview(Guid? MonthId, long? Version, decimal GrossObligation,
    decimal Credits, NpmOpeningMonthRateEvidence Authority, bool IsActivated = false);
public sealed record NpmOpeningMonthActivationRequest(Guid StallId, Guid ContractId, DateOnly PeriodStart,
    long ExpectedVersion, string DecisionReference);
