using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorById;

public record GetCollectorByIdQuery(Guid CollectorId) : IRequest<Result<CollectorActivityDto>>;
