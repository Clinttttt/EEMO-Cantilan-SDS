using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;

public class AddTransporterCommandHandler(
    ITrmRepository trmRepo,
    IUnitOfWork uow) : IRequestHandler<AddTransporterCommand, Result<TrmTransporterDto>>
{
    public async Task<Result<TrmTransporterDto>> Handle(AddTransporterCommand request, CancellationToken ct)
    {
        var transporter = TrmTransporter.Create(
            request.Name,
            request.Organization,
            request.DefaultRoute,
            request.PlateNumber,
            request.Remarks);

        await trmRepo.AddTransporterAsync(transporter, ct);
        await uow.SaveChangesAsync(ct);

        return Result<TrmTransporterDto>.Success(new TrmTransporterDto
        {
            Id = transporter.Id,
            Name = transporter.Name,
            Organization = transporter.Organization,
            DefaultRoute = transporter.DefaultRoute,
            PlateNumber = transporter.PlateNumber,
            IsActive = transporter.IsActive
        });
    }
}
