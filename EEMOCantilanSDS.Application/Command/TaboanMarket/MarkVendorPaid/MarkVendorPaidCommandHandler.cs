using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public class MarkVendorPaidCommandHandler(
    ITpmRepository tpmRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<MarkVendorPaidCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(MarkVendorPaidCommand request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetAttendanceByIdAsync(request.AttendanceId, ct);
        if (attendance == null)
            return Result<bool>.NotFound();

        var recordedBy = currentUser.Username ?? "Admin";
        if (request.IsPaid)
            attendance.MarkPaid(currentUser.CollectorId, updatedBy: recordedBy);
        else
            attendance.MarkUnpaid(recordedBy);

        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
