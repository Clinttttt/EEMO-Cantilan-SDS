using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Reports;

/// <summary>
/// The admin Follow-up Queue payload — an ACTION list (not a finance view) composed from the same
/// canonical sources used by the dashboard and reports: delinquency/arrears, per-facility stall
/// compliance, NPM utility balances, the NPM daily streak, online payments awaiting OR, service-facility receipts missing an
/// OR, and contract expiry. Scope is "as of today". No money KPIs are introduced here — only the
/// actionable items and their counts.
/// </summary>
public record FollowUpQueueDto(
    string PeriodLabel,
    DateOnly AsOf,
    IReadOnlyList<FollowUpItemDto> Items
);

/// <summary>
/// One actionable row. <see cref="Section"/> (1–4) groups by urgency band; <see cref="Priority"/> and
/// <see cref="ReasonKind"/> drive the UI badge/filter. <see cref="Amount"/> is null when no money applies
/// (e.g. contract / operational items); <see cref="Excused"/> flags the "₱0 excused" presentation so an
/// excused record is never shown as a debt. <see cref="Link"/> is a ready-to-use client route.
/// </summary>
public record FollowUpItemDto(
    int Section,
    string Priority,     // Critical | High | Normal | Review
    string Reason,       // display label, e.g. "Delinquent", "Missing OR"
    string ReasonKind,   // filter key: delinquent | arrears | missingor | current | contract | npm | excused | misc
    FacilityCode Facility,
    string Model,        // "Daily stall" | "Monthly rental" | "Per-head" | "Per-trip" | "Weekly market"
    string Person,
    string Identifier,
    decimal? Amount,
    bool Excused,
    string Period,
    string Status,
    string Action,       // button label, e.g. "View vendor", "Encode OR"
    string Link,         // client route, e.g. "/profile/bbq/4"
    Guid? StallId = null, // present for rows that act on a specific stall (e.g. inline daily Add-OR)

    /// <summary>
    /// For a utility row: whether the stall is actually billed for electricity / water (its
    /// <c>ApplicableFees</c> flags). The queue opens the utility-bill dialog, which must offer only the
    /// services the vendor has — a stall registered with water alone has no electricity meter to read.
    /// Both default to false because they are meaningless on non-utility rows.
    /// </summary>
    bool StallHasElectricity = false,
    bool StallHasWater = false
);
