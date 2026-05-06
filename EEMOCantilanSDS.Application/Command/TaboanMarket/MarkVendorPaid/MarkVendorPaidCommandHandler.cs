using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public class MarkVendorPaidCommandHandler(
    ITpmRepository tpmRepo,
    IUnitOfWork uow) : IRequestHandler<MarkVendorPaidCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(MarkVendorPaidCommand request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetAttendanceByIdAsync(request.AttendanceId, ct);
        if (attendance == null)
            return Result<bool>.NotFound();

        if (request.IsPaid)
            attendance.MarkPaid(request.CollectorId);
        else
            attendance.MarkUnpaid();

        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
