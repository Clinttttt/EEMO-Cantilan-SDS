namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>One page of the Head's direct-collection review. Server-filtered, counted and paged; read from the canonical Collections.</summary>
public sealed record DirectCollectionReviewRequest(
    string State = "Pending", DateOnly? From = null, DateOnly? To = null, string? Operation = null, string? Collector = null,
    string? Search = null, int Skip = 0, int Take = 25,
    // The Head's Collection Review area: only direct ECF / WCF amounts and Fish / Meat payors still without a registered vendor.
    bool Scoped = false);

/// <summary>
/// A canonical Collection recorded directly by a Collector (no office-prepared amount), with its administrative review state.
/// <see cref="Disposition"/> is the financial state (Posted / Reversed / Document corrected); <see cref="ReviewState"/> is only whether the
/// Head has looked at it. Review never changes either the money or the SRC.
/// </summary>
public sealed record DirectCollectionReviewRowDto(
    Guid CollectionId, string ReferenceCode, DateOnly BusinessDate, DateTime RecordedAtUtc, string Operation, string? Utility,
    string Payor, string SourceContext, decimal Amount, string RecordedBy, string Mode, string SourceState, string Disposition,
    string ReviewState, string? ReviewNote, string? ReviewedBy, DateTime? ReviewedAtUtc, bool PossibleDuplicate, bool CanConfirm,
    string? Registration = null,
    // Why Confirm is unavailable (null when it is available): the same rule set for one-item and bulk confirmation.
    string? ConfirmBlock = null,
    // The cash described for the web: Verified / Awaiting verification / Needs correction / Awaiting confirmation / Reversed. Cash is received in every state.
    string Verification = "",
    // Collected less valid corrections: a reversed receipt is zero, never a positive verified amount.
    decimal NetAmount = 0m);

/// <summary>
/// Recorded, verified and pending-verification Collector direct-collection cash for a period, as separate figures: Recorded = Verified + Pending, all net of
/// valid corrections. Recorded is the actual cash and never replaced by the verified total.
/// </summary>
public sealed record DirectCollectionVerificationPositionDto(
    DateOnly From, DateOnly To, decimal Recorded, decimal Verified, decimal PendingVerification, decimal AwaitingReview, decimal NeedsCorrection,
    decimal ResolvedAwaitingConfirmation, int CollectionCount, int VerifiedCount, int PendingCount);

/// <summary><paramref name="CanVerify"/> is the server's answer for the signed-in user: only the Head confirms, resolves or bulk-confirms.</summary>
public sealed record DirectCollectionReviewPageDto(IReadOnlyList<DirectCollectionReviewRowDto> Rows, int Total, int PendingCount, int NeedsRegistrationCount = 0, bool CanVerify = true);

public sealed record DirectCollectionDetailLineDto(string Description, decimal Amount);
public sealed record DirectCollectionHistoryDto(string Outcome, string? Note, string Actor, DateTime OccurredAtUtc);

/// <summary>Everything the Head sees when opening one direct collection: the row, its document, its lines and its append-only review history.</summary>
public sealed record DirectCollectionDetailDto(
    DirectCollectionReviewRowDto Row, string? DocumentNumber, string? DocumentKind,
    IReadOnlyList<DirectCollectionDetailLineDto> Lines, IReadOnlyList<DirectCollectionHistoryDto> History);

public sealed record DirectCollectionReviewActionRequest(Guid ClientOperationId, Guid CollectionId, string Outcome, string? Note = null);
public sealed record DirectCollectionBulkConfirmRequest(Guid ClientOperationId, IReadOnlyList<Guid> CollectionIds);
public sealed record DirectCollectionBulkConfirmResult(int Confirmed, IReadOnlyList<DirectCollectionBulkSkip> Skipped);
public sealed record DirectCollectionBulkSkip(Guid CollectionId, string Reason);
