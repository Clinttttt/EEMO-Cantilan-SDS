using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallStatement;

public record GetStallStatementQuery(Guid StallId) : IRequest<Result<StallStatementDto>>;
