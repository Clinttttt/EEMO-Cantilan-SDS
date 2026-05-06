using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporterProfile;

public record GetTransporterProfileQuery(Guid TransporterId) : IRequest<Result<TrmTransporterProfileDto>>;
