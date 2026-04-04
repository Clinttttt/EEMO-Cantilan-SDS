using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacility;

public record GetStallsByFacilityQuery(FacilityCode FacilityCode, MarketSection? Section) : IRequest<Result<IReadOnlyList<StallDto>>>;
