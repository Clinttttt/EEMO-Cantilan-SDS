using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;

public record GetFacilityHistoryQuery(
    FacilityCode FacilityCode,
    int Year
) : IRequest<Result<FacilityHistoryDto>>;
