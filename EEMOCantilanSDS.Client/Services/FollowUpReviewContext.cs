using EEMOCantilanSDS.Application.Dtos.Reports;

namespace EEMOCantilanSDS.Client.Services;

// Presentation context only. Source IDs are supplied by the authoritative queue, never matched by name.
public sealed record FollowUpReviewContext(FollowUpItemDto? FacilityItem, ObligationFollowUpItemDto? ObligationItem,
    DateOnly SnapshotDate, bool CanRecordPayment = false, int? PeriodYear = null, int? PeriodMonth = null)
{
    public bool IsAccount => ObligationItem is not null || FacilityItem is { StallId: not null, ReasonKind: "delinquent" or "current" or "contract" or "npm" };
    public string Name => ObligationItem?.PayerName ?? FacilityItem?.Person ?? "Registered account";
    public string Reference => ObligationItem?.SpaceNumber ?? FacilityItem?.Identifier ?? "";
    public string Link => ObligationItem?.Link ?? FacilityItem?.Link ?? "";
}
