using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalities;

/// <summary>Read-only list of registered municipalities for the public selector.</summary>
public record GetMunicipalitiesQuery() : IRequest<Result<IReadOnlyList<MunicipalityDto>>>;
