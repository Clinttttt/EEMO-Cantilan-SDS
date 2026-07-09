using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddFacility;

/// <summary>
/// Adds one of the standard facility types (by code) to the CURRENT tenant, with a Head-chosen name and
/// short name. Additive and tenant-scoped: it only creates the LGU's own <c>Facility</c> row (billing
/// archetype defaulted from the code); fixed rates fall back to the ordinance defaults until customised,
/// and monthly-rental rates live per stall. Rejected if that code is already configured for the tenant.
/// </summary>
public record AddFacilityCommand(string Code, string Name, string ShortName, string? Description = null)
    : IRequest<Result<bool>>;
