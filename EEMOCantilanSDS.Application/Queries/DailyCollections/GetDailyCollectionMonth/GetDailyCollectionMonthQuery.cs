using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;

public record GetDailyCollectionMonthQuery(
    Guid StallId,
    int Year,
    int Month
) : IRequest<Result<DailyCollectionMonthDto>>;
