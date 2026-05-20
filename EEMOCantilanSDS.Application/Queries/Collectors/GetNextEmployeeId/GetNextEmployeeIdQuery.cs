using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetNextEmployeeId;

public record GetNextEmployeeIdQuery() : IRequest<Result<string>>;
