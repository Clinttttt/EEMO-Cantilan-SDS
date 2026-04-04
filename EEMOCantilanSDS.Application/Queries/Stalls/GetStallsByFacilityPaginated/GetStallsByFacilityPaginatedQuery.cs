using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacilityPaginated;

public record GetStallsByFacilityPaginatedQuery(
    FacilityCode FacilityCode, 
    MarketSection? Section, 
    DateTime? Cursor, 
    int PageSize = 20
) : IRequest<Result<CursorPagedResult<StallDto>>>;
