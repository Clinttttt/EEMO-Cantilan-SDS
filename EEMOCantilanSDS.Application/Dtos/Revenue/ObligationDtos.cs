using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>One assessed (or assessable) period of a specialized obligation account, with its canonical balance.</summary>
public sealed record ObligationQuoteDto(
    Guid AccountId,
    Guid? PeriodId,
    ObligationKind Kind,
    string KindLabel,
    string SubjectLabel,
    DateOnly PeriodStart,
    decimal AssessedAmount,
    decimal SettledAmount,
    decimal OutstandingAmount,
    Guid PayorId,
    string? PayerName,
    Guid? RateId,
    bool CanAddToDraft,
    LotRentalEvent? Event = null);

public sealed record ObligationAccountDto(
    Guid Id,
    ObligationKind Kind,
    string KindLabel,
    Guid PayorId,
    string? PayerName,
    Guid? StallId,
    string? StallNo,
    string SubjectLabel,
    LotRentalEvent? Event,
    DateOnly? EventDate,
    DateOnly ActiveFrom,
    DateOnly? ActiveTo,
    decimal? CurrentAmount,
    DateOnly? CurrentAmountEffectiveFrom,
    decimal AssessedToDate,
    decimal CollectedToDate,
    decimal OutstandingToDate,
    OccupancyArrangement? Arrangement = null,
    string? ContractReference = null,
    string? ActualOccupant = null,
    string? ContractName = null,
    DateOnly? ContractEffectiveDate = null,
    int? ContractYears = null,
    decimal? AreaSqm = null,
    string? AreaLocation = null,
    // The monthly rental the signed contract states (evidence only; the approved amount above is what is assessed).
    decimal? ContractMonthlyRate = null,
    // Retired from the roll (closed, nothing owed, records kept). Part of the one account-state classification.
    bool Retired = false,
    // A renewed space's terms, oldest first, each with its own figures (they add back to the totals above). Null when never renewed.
    IReadOnlyList<SpaceTermDto>? Terms = null);

/// <summary>
/// One contract term of a renewed space account. <see cref="RenewedOn"/> is the day before the next term began (null for the term in
/// force); periods up to it belong to this term, including any lapsed months before the renewal.
/// </summary>
public sealed record SpaceTermDto(
    int Number,
    DateOnly Commencement,
    int? Years,
    DateOnly? LastDay,
    DateOnly? RenewedOn,
    string? ContractName,
    string? Reference,
    decimal Assessed,
    decimal Collected,
    decimal Outstanding,
    bool Current);

public sealed record CreateObligationAccountRequest(
    ObligationKind Kind,
    Guid PayorId,
    Guid? StallId,
    string SubjectLabel,
    LotRentalEvent? Event,
    DateOnly? EventDate,
    DateOnly ActiveFrom,
    decimal Amount,
    OccupancyArrangement? Arrangement = null,
    string? ContractReference = null,
    string? ActualOccupant = null,
    string? ContractName = null,
    DateOnly? ContractEffectiveDate = null,
    int? ContractYears = null,
    decimal? AreaSqm = null,
    string? AreaLocation = null,
    decimal? ContractMonthlyRate = null,
    // Retired from the roll (closed, nothing owed, records kept). Part of the one account-state classification.
    bool Retired = false);

/// <summary>Head-authorized correction of recorded holder/lease evidence; it never changes existing financial periods.</summary>
public sealed record CorrectObligationHolderRequest(string ActualOccupant, OccupancyArrangement Arrangement,
    string? ContractReference, string? ContractName, DateOnly? ContractEffectiveDate,
    int? ContractYears, decimal? AreaSqm, string? AreaLocation, decimal? ContractMonthlyRate = null,
    SetObligationRateRequest? RateChange = null);

public sealed record SetObligationRateRequest(DateOnly EffectiveFrom, decimal Amount);

public sealed record CloseObligationAccountRequest(DateOnly ActiveTo);

public sealed record ObligationWorkspaceDto(IReadOnlyList<ObligationAccountDto> Accounts,
    decimal Assessed, decimal Collected, decimal Outstanding);

public sealed record ImportSpaceHolderRow(CreateObligationAccountRequest Account, DateOnly? ClosedOn = null);
/// <summary>The reviewed rows to save. <paramref name="ClientOperationId"/> identifies this submission: sending the same one again (a double click, a retry after a lost response) returns the saved result instead of registering the holders twice.</summary>
public sealed record ImportSpaceHoldersRequest(IReadOnlyList<ImportSpaceHolderRow> Rows, Guid? ClientOperationId = null);
public enum SpaceHolderImportStatus { Ready = 1, NeedsPayor = 2, Invalid = 3, NeedsSourceHolder = 4 }
public enum SpaceNumberOrigin { Supplied = 1, ServerSuggested = 2 }
public enum SpaceHolderImportAction { None = 0, SelectPayor = 1, CorrectRow = 2, ResolveDuplicate = 3, ConfirmSourceHolder = 4 }
public sealed record SpaceHolderImportFacts(CreateObligationAccountRequest Account, DateOnly? ClosedOn,
    SpaceNumberOrigin NumberOrigin, string? PayorDisplayName);
public sealed record SpaceHolderImportRowResult(int RowNumber, SpaceHolderImportStatus Status, string? Code,
    string? Message, Guid? AccountId = null, SpaceHolderImportFacts? Facts = null)
{
    public SpaceHolderImportAction RequiredAction => Status == SpaceHolderImportStatus.NeedsSourceHolder ? SpaceHolderImportAction.ConfirmSourceHolder
        : Status == SpaceHolderImportStatus.NeedsPayor || Code == "InvalidPayor"
        ? SpaceHolderImportAction.SelectPayor : Code == "DuplicateSpace" ? SpaceHolderImportAction.ResolveDuplicate
        : Status == SpaceHolderImportStatus.Invalid ? SpaceHolderImportAction.CorrectRow : SpaceHolderImportAction.None;
}
public sealed record SpaceHolderImportPreview(IReadOnlyList<SpaceHolderImportRowResult> Rows, bool CanSave);
public sealed record SpaceRentalOperationDto(ObligationKind Kind, LotRentalEvent? Event, string DisplayName, bool IsMonthly);
public sealed record ImportSpaceHoldersResult(int Imported, int Skipped, IReadOnlyList<string> NeedsReview,
    IReadOnlyList<SpaceHolderImportRowResult>? Rows = null);

public sealed record AddObligationDraftAllocationRequest(
    Guid AccountId,
    int BillingYear,
    int BillingMonth,
    decimal ProposedAmount,
    long? ExpectedRevision);

/// <summary>A vendor-fee-eligible NPM Fish/Meat stall and the explicitly linked Payor of its current occupancy.</summary>
public sealed record VendorFeeStallDto(Guid StallId, string StallNo, string Section, Guid? PayorId, string? PayerName, bool HasAccount);

/// <summary>
/// One posted Collection's allocation to one period of one space-rental account, read from the canonical ledger (never from a copy).
/// <see cref="Amount"/> is what the Collection allocated to the period; <see cref="CorrectionEffect"/> is the net of any correction against
/// that allocation (negative for a reversal), so <see cref="NetAmount"/> is what the period actually keeps. The original SRC always stays.
/// </summary>
public sealed record ObligationPaymentDto(
    Guid CollectionId, string ReferenceCode, DateOnly BusinessDate, DateTime RecordedAtUtc, DateOnly PeriodStart,
    decimal Amount, decimal CorrectionEffect, decimal NetAmount, RevenueInstrumentType? Instrument, string RecordedBy, string Disposition,
    IReadOnlyList<ObligationPaymentCorrectionDto>? Corrections = null, IReadOnlyList<string>? ReplacementReferenceCodes = null);

/// <summary>A correction recorded against a posted Collection: what kind, why, when, and its net financial effect. The original SRC is never altered by it.</summary>
public sealed record ObligationPaymentCorrectionDto(string CorrectionType, string Reason, DateOnly EffectiveDate, DateTime RecordedAtUtc, decimal FinancialEffect);

/// <summary>A saved bulk import of space holders (never a preview or a failed attempt).</summary>
public sealed record SpaceHolderImportRunDto(
    Guid Id, ObligationKind Kind, DateTime OccurredAtUtc, string ImportedBy, int RowCount, int ImportedCount, int SkippedCount, string? SkippedSummary);
