namespace EEMOCantilanSDS.Application.Dtos.Audit;

/// <summary>
/// An Actor filter option: <see cref="Value"/> is the stored username used for filtering,
/// <see cref="Label"/> is the human-friendly name shown in the dropdown.
/// </summary>
public record AuditActorOptionDto(string Value, string Label);
