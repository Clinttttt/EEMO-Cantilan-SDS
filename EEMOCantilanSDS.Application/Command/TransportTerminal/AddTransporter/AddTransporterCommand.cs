using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;

public record AddTransporterCommand(
    string Name,
    string Organization,
    string DefaultRoute,
    string? Remarks
) : IRequest<Result<TrmTransporterDto>>;
