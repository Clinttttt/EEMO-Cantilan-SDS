using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetNpmCustomSections;

/// <summary>Lists the current tenant's NPM custom sections (name + stall count) for the NPM page.</summary>
public record GetNpmCustomSectionsQuery() : IRequest<Result<IReadOnlyList<NpmCustomSectionDto>>>;
