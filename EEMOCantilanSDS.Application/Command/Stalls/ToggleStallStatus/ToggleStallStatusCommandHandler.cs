using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;

public class ToggleStallStatusCommandHandler(IStallRepository stallRepository, IUnitOfWork unitOfWork) : IRequestHandler<ToggleStallStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ToggleStallStatusCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall == null)
            return Result<bool>.NotFound();

        if (request.Close)
            stall.Close();
        else
            stall.Reopen();

        await stallRepository.UpdateAsync(stall, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
