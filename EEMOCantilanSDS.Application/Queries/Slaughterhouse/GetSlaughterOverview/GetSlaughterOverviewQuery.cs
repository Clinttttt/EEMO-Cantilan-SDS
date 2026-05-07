using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;

public record GetSlaughterOverviewQuery(
    int Year,
    int Month
) : IRequest<Result<SlaughterOverviewDto>>;
