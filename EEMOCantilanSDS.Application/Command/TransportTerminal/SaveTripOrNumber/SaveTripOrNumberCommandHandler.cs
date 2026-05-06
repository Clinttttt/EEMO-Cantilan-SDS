using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.SaveTripOrNumber;

public class SaveTripOrNumberCommandHandler(
    ITrmRepository trmRepo,
    IUnitOfWork uow) : IRequestHandler<SaveTripOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveTripOrNumberCommand request, CancellationToken ct)
    {
        var trip = await trmRepo.GetTripByIdAsync(request.TripId, ct);
        if (trip == null)
            return Result<bool>.NotFound();

        trip.UpdateORNumber(request.ORNumber, "Admin");
        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
