namespace EEMOCantilanSDS.Application.Dtos.Audit;

/// <summary>
/// One Head-authorized audit event. Contains only reviewed presentation fields, never raw
/// database snapshots, gateway responses, credentials, or token contents.
/// </summary>
public sealed record AuditEventDetailsDto(
    Guid Id,
    DateTime LoggedAtUtc,
    string ActorName,
    string ActorDisplayName,
    string ActorRole,
    string Action,
    string EntityType,
    Guid? EntityId,
    string Details,
    IReadOnlyList<string> Changes);
