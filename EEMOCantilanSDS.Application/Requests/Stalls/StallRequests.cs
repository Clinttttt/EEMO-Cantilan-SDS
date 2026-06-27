namespace EEMOCantilanSDS.Application.Requests.Stalls;

public record ToggleStallStatusRequest(bool Close);

/// <summary>
/// Renew an inactive (expired) stall account: starts a NEW contract term for the occupant.
/// "Proceed" sends the same occupant/name/duration with today's start; "Edit" sends adjusted values.
/// </summary>
public record RenewStallContractRequest(
    DateOnly EffectivityDate,
    int DurationYears,
    string ActualOccupant,
    string? NameOnContract);
