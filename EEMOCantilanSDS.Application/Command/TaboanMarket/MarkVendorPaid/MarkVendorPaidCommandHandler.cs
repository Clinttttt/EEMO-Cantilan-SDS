using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public class MarkVendorPaidCommandHandler(
    ITpmRepository tpmRepo,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<MarkVendorPaidCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(MarkVendorPaidCommand request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetAttendanceByIdAsync(request.AttendanceId, ct);
        if (attendance == null)
            return Result<bool>.NotFound();

        // Collectors may only collect at Tabo-an if assigned to it; admins are unrestricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<bool>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == Domain.Enums.FacilityCode.TPM))
            {
                return Result<bool>.Forbidden();
            }
        }

        var recordedBy = currentUser.Username ?? "Admin";
        if (request.IsPaid)
        {
            attendance.MarkPaid(currentUser.CollectorId, updatedBy: recordedBy);
            if (!string.IsNullOrWhiteSpace(request.ORNumber))
                attendance.SetORNumber(request.ORNumber.Trim(), recordedBy);
        }
        else
        {
            attendance.MarkUnpaid(recordedBy);
        }

        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
