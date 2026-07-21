namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// A per-LGU custom NPM section: its display name and how many stalls currently belong to it. The stall
/// count gates removal — a custom section can only be removed from the registry when no stall uses it.
/// </summary>
public record NpmCustomSectionDto(string Name, int StallCount);
