namespace EEMOCantilanSDS.Application.Dtos.Collections;

/// <summary>Why a canonical Collection can, or cannot, be corrected by the Head today. Codes are machine values; the text is for people.</summary>
public sealed record CollectionCorrectionEligibilityDto(bool CanCorrect, string? BlockCode, string Explanation);

/// <summary>Server-side review facts for one canonical SRC that the activity feed does not already carry.</summary>
public sealed record CollectionReviewDto(
    Guid CollectionId,
    string ReferenceCode,
    DateOnly BusinessDate,
    bool Remitted,
    bool HasCorrection,
    CollectionCorrectionEligibilityDto Eligibility,
    // No collector recorded this receipt (Office-origin). Its formal turnover/remittance status is not established by the workflow.
    bool OfficeOrigin = false);

/// <summary>Head-only reversal of one eligible canonical rent receipt. The operation ID makes a retry return the original outcome.</summary>
public sealed record HeadReverseCollectionRequest(Guid ClientOperationId, Guid CollectionId, string Reason);

public sealed record HeadCorrectionResultDto(
    Guid CorrectionId,
    Guid CollectionId,
    string ReferenceCode,
    decimal ReopenedAmount,
    bool ExistingOutcome);
