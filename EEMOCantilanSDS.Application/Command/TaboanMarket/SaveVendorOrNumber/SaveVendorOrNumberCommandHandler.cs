using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.SaveVendorOrNumber;

public class SaveVendorOrNumberCommandHandler(
    ITpmRepository tpmRepo,
    IUnitOfWork uow) : IRequestHandler<SaveVendorOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveVendorOrNumberCommand request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetAttendanceByIdAsync(request.AttendanceId, ct);
        if (attendance == null)
            return Result<bool>.NotFound();

        attendance.SetORNumber(request.ORNumber);
        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
