namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>One page of the Head's direct-collection review. Server-filtered, counted and paged; read from the canonical Collections.</summary>
public sealed record DirectCollectionReviewRequest(
    string State = "Pending", DateOnly? From = null, DateOnly? To = null, string? Operation = null, string? Collector = null,
    string? Search = null, int Skip = 0, int Take = 25);

/// <summary>
/// A canonical Collection recorded directly by a Collector (no office-prepared amount), with its administrative review state.
/// <see cref="Disposition"/> is the financial state (Posted / Reversed / Document corrected); <see cref="ReviewState"/> is only whether the
/// Head has looked at it. Review never changes either the money or the SRC.
/// </summary>
public sealed record DirectCollectionReviewRowDto(
    Guid CollectionId, string ReferenceCode, DateOnly BusinessDate, DateTime RecordedAtUtc, string Operation, string? Utility,
    string Payor, string SourceContext, decimal Amount, string RecordedBy, string Mode, string SourceState, string Disposition,
    string ReviewState, string? ReviewNote, string? ReviewedBy, DateTime? ReviewedAtUtc, bool PossibleDuplicate, bool CanConfirm);

public sealed record DirectCollectionReviewPageDto(IReadOnlyList<DirectCollectionReviewRowDto> Rows, int Total, int PendingCount);

public sealed record DirectCollectionReviewActionRequest(Guid ClientOperationId, Guid CollectionId, string Outcome, string? Note = null);
public sealed record DirectCollectionBulkConfirmRequest(Guid ClientOperationId, IReadOnlyList<Guid> CollectionIds);
public sealed record DirectCollectionBulkConfirmResult(int Confirmed, IReadOnlyList<DirectCollectionBulkSkip> Skipped);
public sealed record DirectCollectionBulkSkip(Guid CollectionId, string Reason);
