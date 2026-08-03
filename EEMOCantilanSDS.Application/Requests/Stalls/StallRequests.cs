namespace EEMOCantilanSDS.Application.Requests.Stalls;

public record ToggleStallStatusRequest(bool Close);

/// <summary>
/// Renew an inactive (expired) stall account: starts a NEW contract term for the occupant.
/// "Proceed" sends the same occupant/name/duration with today's start; "Edit" sends adjusted values —
/// including, when the office corrects them, the rent, the measured area and the area note. Anything
/// left null keeps what the stall already carries.
/// </summary>
public record RenewStallContractRequest(
    DateOnly EffectivityDate,
    int DurationYears,
    string ActualOccupant,
    string? NameOnContract,
    decimal? MonthlyRate = null,
    double? AreaSqm = null,
    string? AreaNote = null);
