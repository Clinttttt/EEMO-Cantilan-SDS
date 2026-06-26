using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;

public class SetNpmMarketClosureCommandHandler(
    INpmMarketClosureRepository closureRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<SetNpmMarketClosureCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetNpmMarketClosureCommand request, CancellationToken ct)
    {
        var recordedBy = currentUser.Username ?? "Admin";
        var existing = await closureRepository.GetAsync(request.Date, ct);

        if (existing is null)
        {
            var closure = NpmMarketClosure.Create(request.Date, request.Reason, request.Remarks, recordedBy);
            await closureRepository.AddAsync(closure, ct);
        }
        else
        {
            existing.Update(request.Reason, request.Remarks, recordedBy);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
