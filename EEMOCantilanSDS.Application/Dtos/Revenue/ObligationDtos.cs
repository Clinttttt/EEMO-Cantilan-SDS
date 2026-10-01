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
    bool CanAddToDraft);

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
    decimal OutstandingToDate);

public sealed record CreateObligationAccountRequest(
    ObligationKind Kind,
    Guid PayorId,
    Guid? StallId,
    string SubjectLabel,
    LotRentalEvent? Event,
    DateOnly? EventDate,
    DateOnly ActiveFrom,
    decimal Amount);

public sealed record SetObligationRateRequest(DateOnly EffectiveFrom, decimal Amount);

public sealed record CloseObligationAccountRequest(DateOnly ActiveTo);

public sealed record AddObligationDraftAllocationRequest(
    Guid AccountId,
    int BillingYear,
    int BillingMonth,
    decimal ProposedAmount,
    long? ExpectedRevision);

/// <summary>A vendor-fee-eligible NPM Fish/Meat stall and the explicitly linked Payor of its current occupancy.</summary>
public sealed record VendorFeeStallDto(Guid StallId, string StallNo, string Section, Guid? PayorId, string? PayerName, bool HasAccount);
