using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// Rollout gate for Office Cash Turnover. Default off: nothing is read or written until the schema is installed and activation is approved.
/// <paramref name="Tenants"/> limits it to named tenants; null means every tenant (tests only; configuration always names them).
/// </summary>
public sealed record OfficeCashTurnoverOptions(bool Enabled = false, IReadOnlySet<Guid>? Tenants = null)
{
    public bool Allows(Guid tenant) => Enabled && tenant != Guid.Empty && (Tenants is null || Tenants.Contains(tenant));
}

/// <summary>One Office-origin Collection that still has cash to account for: original amount, linked corrections and the net.</summary>
public sealed record OfficeCashCollectionDto(
    Guid CollectionId,
    string ReferenceCode,
    DateOnly BusinessDate,
    string? PayerName,
    decimal Amount,
    decimal CorrectionEffect,
    decimal NetAmount,
    string OriginalActorName);

public sealed record OfficeCashTurnoverReviewRequest(DateOnly From, DateOnly To);

public sealed record OfficeCashFilterRequest(DateOnly From, DateOnly To, Guid? AuthorityId = null, RevenueInstrumentType? Instrument = null);
public sealed record OfficeCashAuthorityDto(Guid Id, string Name);
public sealed record OfficeCashWorkspaceDto(OfficeCashPositionDto Position, OfficeCashTurnoverReviewDto Review,
    IReadOnlyList<OfficeCashTurnoverRowDto> Register, IReadOnlyList<OfficeCashAuthorityDto> Authorities);

/// <summary>
/// Office-origin cash for a period, apart from any collector figure: collected (net of corrections), covered by an active turnover, and pending.
/// Covered is what was expected of the covered Collections, not what was handed over; shortfalls and excess stay on each turnover.
/// </summary>
public sealed record OfficeCashPositionDto(DateOnly From, DateOnly To, decimal Collected, decimal Covered, decimal Pending, int CollectionCount, int PendingCount, decimal TurnedOverAmount, decimal NetDifference, int NeedsReviewCount);

public sealed record OfficeCashTurnoverReviewDto(DateOnly From, DateOnly To, IReadOnlyList<OfficeCashCollectionDto> Collections, decimal ExpectedAmount);

/// <summary>Head-only. <paramref name="Reference"/> is an optional external supporting-document reference (never required, never verified); StallTrack generates the internal turnover reference itself.</summary>
public sealed record RecordOfficeCashTurnoverRequest(
    Guid ClientOperationId,
    DateOnly TurnoverDate,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? CollectionIds,
    decimal AmountTurnedOver,
    string? Reference,
    string? TurnedOverBy,
    string? Remarks);

public sealed record VoidOfficeCashTurnoverRequest(string Reason);

public sealed record OfficeCashTurnoverRowDto(
    Guid Id,
    DateOnly TurnoverDate,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal ExpectedAmount,
    decimal TurnedOverAmount,
    decimal DifferenceAmount,
    bool NeedsReview,
    string ReferenceCode,
    string? Reference,
    string TurnedOverBy,
    OfficeCashTurnoverStatus Status,
    int CollectionCount,
    string RecordedBy,
    string RecordedByActorId,
    DateTime RecordedAtUtc,
    string? VoidReason,
    string? VoidedBy,
    string? VoidedByActorId,
    DateTime? VoidedAtUtc);

public sealed record OfficeCashTurnoverCollectionDto(
    Guid CollectionId,
    string ReferenceCode,
    decimal CoveredAmount,
    string OriginalActorId,
    string OriginalActorName,
    bool IsActive);

public sealed record OfficeCashTurnoverDetailDto(OfficeCashTurnoverRowDto Row, string? Remarks, IReadOnlyList<OfficeCashTurnoverCollectionDto> Collections);
