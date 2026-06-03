using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummaries;

public record GetFacilitySummariesQuery(int Year, int Month) : IRequest<Result<IReadOnlyList<FacilitySidebarSummaryDto>>>;
