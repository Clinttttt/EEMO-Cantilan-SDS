using EEMOCantilanSDS.Application.Dtos.Reports;

namespace EEMOCantilanSDS.Client.Services;

// Presentation context only. Source IDs are supplied by the authoritative queue, never matched by name.
public sealed record FollowUpReviewContext(FollowUpItemDto? FacilityItem, ObligationFollowUpItemDto? ObligationItem,
    DateOnly SnapshotDate, bool CanRecordPayment = false, int? PeriodYear = null, int? PeriodMonth = null)
{
    public bool IsAccount => ObligationItem is not null || FacilityItem is { StallId: not null, ReasonKind: "delinquent" or "arrears" or "current" or "contract" or "npm" };
    public string Name => ObligationItem?.PayerName ?? FacilityItem?.Person ?? "Registered account";
    public string Reference => ObligationItem?.SpaceNumber ?? FacilityItem?.Identifier ?? "";
    public string Link => ObligationItem?.Link ?? FacilityItem?.Link ?? "";

    /// <summary>
    /// The review to return to after the queue reloaded behind a payment or an OR: the same row as the queue now holds it, so
    /// its amount is today's and not the one shown before. Null when the row left the queue (nothing is left to review);
    /// unchanged when there is no reloaded queue to read.
    /// </summary>
    public FollowUpReviewContext? Reloaded(IReadOnlyList<FollowUpItemDto>? items, Func<FollowUpItemDto, bool> canRecordPayment)
    {
        if (FacilityItem is not { } before || items is null) return this;
        // Not by period: paying the oldest month moves the row's period on, and it is still the same row.
        var now = items.FirstOrDefault(i => i.StallId == before.StallId && i.Facility == before.Facility
            && i.ReasonKind == before.ReasonKind && i.Identifier == before.Identifier);
        return now is null ? null : this with { FacilityItem = now, CanRecordPayment = canRecordPayment(now) };
    }
}
