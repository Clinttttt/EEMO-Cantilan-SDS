namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// Rollout state of an LGU in the CARCANMADCARLAN cluster. Cantilan is the live baseline (<see cref="Active"/>);
/// other municipalities remain <see cref="Upcoming"/> until validated and onboarded. The onboarding
/// lifecycle (assessment → validation → activation) can extend this enum in a later phase.
/// </summary>
public enum MunicipalityStatus
{
    Upcoming = 0,
    Active = 1
}
