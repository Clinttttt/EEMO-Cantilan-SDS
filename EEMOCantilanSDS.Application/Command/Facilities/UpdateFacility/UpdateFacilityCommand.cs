using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.UpdateFacility;

/// <summary>
/// Updates a facility's presentation (name, short name, description) for the CURRENT tenant. The code and
/// billing archetype are immutable; only the labels change — e.g. correcting an onboarding naming artifact.
/// </summary>
public record UpdateFacilityCommand(
    string Code,
    string Name,
    string ShortName,
    string? Description = null,
    string? VegetableSectionLabel = null,
    string? FishSectionLabel = null,
    string? MeatSectionLabel = null)
    : IRequest<Result<bool>>;
