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
    string? Notes);
