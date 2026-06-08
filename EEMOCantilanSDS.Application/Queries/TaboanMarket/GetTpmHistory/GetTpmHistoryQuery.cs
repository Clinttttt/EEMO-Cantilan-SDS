using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmHistory;

public record GetTpmHistoryQuery(int Year) : IRequest<Result<TpmHistoryDto>>;
