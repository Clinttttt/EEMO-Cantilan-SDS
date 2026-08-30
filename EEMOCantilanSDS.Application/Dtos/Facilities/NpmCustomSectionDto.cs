namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// A per-LGU custom NPM section: its display name, how many stalls currently belong to it, and the daily fee the office
/// has stated for it. The stall count gates removal — a custom section can only be removed from the registry when no
/// stall uses it.
/// </summary>
/// <param name="DailyRate">
/// The office's stated daily fee for this section as of today, or null where it has stated none and the stalls in it are
/// billed the market's own rate. A stall let at its own rate keeps that rate regardless.
/// </param>
public record NpmCustomSectionDto(string Name, int StallCount, decimal? DailyRate = null);
