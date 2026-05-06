using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMarketDays;

public record GetMarketDaysQuery(int Year, int Month) : IRequest<Result<IReadOnlyList<TpmMarketDayDto>>>;
