using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;

public class SetStallMonthlyExceptionCommandHandler(
    IStallMonthlyExceptionRepository exceptionRepository,
    IStallRepository stallRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<SetStallMonthlyExceptionCommand, Result<bool>>
{
    private static readonly HashSet<FacilityCode> MonthlyFacilities =
        new() { FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE };

    public async Task<Result<bool>> Handle(SetStallMonthlyExceptionCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        // NPM is collected daily (use DailyCollection.IsAbsent); service facilities have no recurring
        // rent. Monthly exceptions apply only to fixed monthly-rental facilities.
        if (stall.Facility?.Code is not { } code || !MonthlyFacilities.Contains(code))
            return Result<bool>.Failure(
                "Monthly exceptions apply to monthly-rental facilities (TCC / NCC / BBQ / ICE) only.", 400);

        var recordedBy = currentUser.Username ?? "Admin";
        var existing = await exceptionRepository.GetAsync(request.StallId, request.Year, request.Month, ct);

        if (existing is null)
        {
            var exception = StallMonthlyException.Create(
                request.StallId, request.Year, request.Month, request.Reason, request.Remarks, recordedBy);
            await exceptionRepository.AddAsync(exception, ct);
        }
        else
        {
            existing.Update(request.Reason, request.Remarks, recordedBy);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
