using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;

public class UpdateStallDetailsCommandHandler(
    IStallRepository stallRepo,
    IUnitOfWork uow) : IRequestHandler<UpdateStallDetailsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateStallDetailsCommand request, CancellationToken cancellationToken)
    {
        var stall = await stallRepo.GetByIdAsync(request.StallId, cancellationToken);
        if (stall == null)
            return Result<bool>.NotFound();

        stall.UpdateDetails(
            request.ActualOccupant,
            request.NameOnContract,
            request.AreaSqm,
            request.AreaNote,
            null,
            "Admin");

        await uow.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
