using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetAllCollectors;

public record GetAllCollectorsQuery : IRequest<Result<IReadOnlyList<CollectorListDto>>>;
