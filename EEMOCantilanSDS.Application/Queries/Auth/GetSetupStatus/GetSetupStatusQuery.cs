using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;

public record GetSetupStatusQuery : IRequest<Result<SetupStatusDto>>;

public record SetupStatusDto(bool IsSetupRequired);
