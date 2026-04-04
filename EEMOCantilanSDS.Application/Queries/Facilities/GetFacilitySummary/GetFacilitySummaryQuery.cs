using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummary;

public record GetFacilitySummaryQuery(FacilityCode FacilityCode, int Year, int Month) : IRequest<Result<FacilitySummaryDto>>;
