namespace EEMOCantilanSDS.Application.Dtos.Audit;

/// <summary>
/// A single audit-trail entry as exposed to the client. Projected from <c>AuditLog</c>;
/// heavy JSON snapshots (OldValues/NewValues) are intentionally not surfaced in the list view.
/// </summary>
public record AuditLogDto(
    Guid Id,
    DateTime LoggedAtUtc,
    string ActorName,         // stored username (used for filtering)
    string ActorDisplayName,  // resolved staff full name, falls back to the username
    string ActorRole,
    string Action,            // "Created" | "Updated" | "Deleted"
    string EntityType,        // entity class name, e.g. "PaymentRecord"
    Guid? EntityId,
    string? Notes,
    /// <summary>
    /// What actually happened, in words, composed from the entry's own snapshot: the payor or account
    /// concerned, the facility and section, the amount, the receipt number. An audit reader must never have to
    /// infer the event from "Updated PaymentRecord".
    /// </summary>
    string Details = "",
    /// <summary>Named fields that changed, for an update — e.g. "Monthly rate ₱900.00 → ₱1,200.00".</summary>
    IReadOnlyList<string>? Changes = null);
