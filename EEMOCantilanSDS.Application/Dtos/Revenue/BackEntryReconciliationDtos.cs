using EEMOCantilanSDS.Application.Common;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// What the office knows about a collection it is asked to enter after the fact. At least one criterion is required: a ClientOperationId
/// (the device's session or operation id), a reference code (SRC), or a collector with a business date (and optionally the amount).
/// </summary>
public sealed record BackEntryLookupRequest(Guid? ClientOperationId = null, string? ReferenceCode = null,
    Guid? CollectorId = null, DateOnly? BusinessDate = null, decimal? Amount = null);

/// <summary>
/// One piece of evidence the server holds. <see cref="Kind"/> is "Collection", "PostingOperation" or "Session"; <see cref="Basis"/> says why it
/// matched ("ClientOperationId", "ReferenceCode", "SessionId", "SameCollectorDateAmount", "SameCollectorDate"); <see cref="State"/> says what it is
/// ("Recorded", "Corrected", "Rejected", "Recorded session").
/// </summary>
public sealed record BackEntryMatchDto(string Kind, string Basis, string State, Guid? CollectionId, string? ReferenceCode,
    DateOnly? BusinessDate, decimal? Amount, Guid? CollectorId, string? CollectorName, Guid? ClientOperationId, string? Detail = null);

/// <summary>
/// The reconciliation answer. <see cref="Verdict"/> is "AlreadyRecorded" (exact evidence the collection exists), "PossibleMatch" (similar evidence an
/// officer must compare), or "NoServerRecord". "NoServerRecord" is NOT proof that nothing was collected: an unsynchronised device may still hold it,
/// so no back-entry may be posted on this answer alone. This is read-only; it records and changes nothing.
/// </summary>
public sealed record BackEntryLookupResult(string Verdict, string Statement, IReadOnlyList<BackEntryMatchDto> Matches);

public interface IBackEntryReconciliationReader
{
    Task<Result<BackEntryLookupResult>> LookupAsync(BackEntryLookupRequest request, CancellationToken ct = default);
}
