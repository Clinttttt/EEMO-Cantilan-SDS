using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterHistory;

public record GetSlaughterHistoryQuery(
    int Year
) : IRequest<Result<SlaughterHistoryDto>>;
