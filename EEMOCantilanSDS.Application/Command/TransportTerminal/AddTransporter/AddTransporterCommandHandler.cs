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
        // A plate number identifies a single vehicle — reuse the existing transporter instead of
        // creating a duplicate (the mobile "Record a Trip" quick flow re-submits the same plate,
        // and admins can otherwise add the same plate twice).
        var existing = await trmRepo.GetTransporterByPlateAsync(request.PlateNumber, ct);
        if (existing is not null)
            return Result<TrmTransporterDto>.Success(ToDto(existing));

        var transporter = TrmTransporter.Create(
            request.Name,
            string.IsNullOrWhiteSpace(request.Organization) ? "Non-associated" : request.Organization.Trim(),
            request.DefaultRoute,
            request.PlateNumber,
            request.Remarks);

        await trmRepo.AddTransporterAsync(transporter, ct);
        await uow.SaveChangesAsync(ct);

        return Result<TrmTransporterDto>.Success(ToDto(transporter));
    }

    private static TrmTransporterDto ToDto(TrmTransporter t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Organization = t.Organization,
        DefaultRoute = t.DefaultRoute,
        PlateNumber = t.PlateNumber,
        IsActive = t.IsActive
    };
}
