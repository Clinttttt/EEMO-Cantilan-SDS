using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;

public class ClearStallMonthlyExceptionCommandHandler(
    IStallMonthlyExceptionRepository exceptionRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<ClearStallMonthlyExceptionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ClearStallMonthlyExceptionCommand request, CancellationToken ct)
    {
        var existing = await exceptionRepository.GetAsync(request.StallId, request.Year, request.Month, ct);
        if (existing is not null)
        {
            exceptionRepository.Remove(existing);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result<bool>.Success(true);
    }
}
