using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmOverview;

public record GetTpmOverviewQuery(int Year, int Month) : IRequest<Result<TpmOverviewDto>>;
