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
    string? ContractReference = null);

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
    string? ContractReference = null);

public sealed record SetObligationRateRequest(DateOnly EffectiveFrom, decimal Amount);

public sealed record CloseObligationAccountRequest(DateOnly ActiveTo);

public sealed record ObligationWorkspaceDto(IReadOnlyList<ObligationAccountDto> Accounts,
    decimal Assessed, decimal Collected, decimal Outstanding);

public sealed record ImportSpaceHolderRow(CreateObligationAccountRequest Account, DateOnly? ClosedOn = null);
public sealed record ImportSpaceHoldersRequest(IReadOnlyList<ImportSpaceHolderRow> Rows);
public enum SpaceHolderImportStatus { Ready = 1, NeedsPayor = 2, Invalid = 3 }
public enum SpaceNumberOrigin { Supplied = 1, ServerSuggested = 2 }
public enum SpaceHolderImportAction { None = 0, SelectPayor = 1, CorrectRow = 2, ResolveDuplicate = 3 }
public sealed record SpaceHolderImportFacts(CreateObligationAccountRequest Account, DateOnly? ClosedOn,
    SpaceNumberOrigin NumberOrigin, string? PayorDisplayName);
public sealed record SpaceHolderImportRowResult(int RowNumber, SpaceHolderImportStatus Status, string? Code,
    string? Message, Guid? AccountId = null, SpaceHolderImportFacts? Facts = null)
{
    public SpaceHolderImportAction RequiredAction => Status == SpaceHolderImportStatus.NeedsPayor || Code == "InvalidPayor"
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
