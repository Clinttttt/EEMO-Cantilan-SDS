using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityConfiguration;

/// <summary>
/// Reads the current tenant's facility configuration (configured facilities + rates, and the canonical
/// types still available to add). Tenant is resolved from the authenticated caller, so no parameters.
/// </summary>
public record GetFacilityConfigurationQuery() : IRequest<Result<FacilityConfigurationDto>>;
