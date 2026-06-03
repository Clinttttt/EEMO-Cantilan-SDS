using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;

public class RecordTripCommandHandler(
    ITrmRepository trmRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<RecordTripCommand, Result<TrmTripDto>>
{
    public async Task<Result<TrmTripDto>> Handle(RecordTripCommand request, CancellationToken ct)
    {
        var transporter = await trmRepo.GetTransporterByIdAsync(request.TransporterId, ct);
        if (transporter == null)
            return Result<TrmTripDto>.NotFound();

        var tripNumber = await trmRepo.GetNextTripNumberForTodayAsync(ct);

        var trip = TrmTrip.Create(
            request.TransporterId,
            tripNumber,
            request.DriverName,
            request.PlateNumber,
            request.Route,
            request.ORNumber,
            collectorId: currentUser.CollectorId,
            remarks: request.Remarks,
            createdBy: currentUser.Username ?? "Admin");

        await trmRepo.AddTripAsync(trip, ct);
        await uow.SaveChangesAsync(ct);

        return Result<TrmTripDto>.Success(new TrmTripDto
        {
            Id = trip.Id,
            TransporterId = trip.TransporterId,
            TripNumber = trip.TripNumber,
            DriverName = trip.DriverName,
            Organization = transporter.Organization,
            PlateNumber = trip.PlateNumber,
            Route = trip.Route,
            Fee = trip.Fee,
            ORNumber = trip.ORNumber,
            RecordedAt = trip.RecordedAt
        });
    }
}
