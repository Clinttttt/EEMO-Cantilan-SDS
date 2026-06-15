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
            var orNumber = request.ORNumber?.Trim();
            if (!string.IsNullOrWhiteSpace(orNumber))
            {
                // Allow re-marking with the OR already on this attendance; reject a new OR used elsewhere.
                var alreadyOnThisRecord = string.Equals(attendance.ORNumber?.Trim(), orNumber, StringComparison.Ordinal);
                if (!alreadyOnThisRecord && !await tpmRepo.IsORNumberUniqueAsync(orNumber, ct))
                    return Result<bool>.Failure("OR number already exists.", 409);
            }

            attendance.MarkPaid(currentUser.CollectorId, updatedBy: recordedBy);
            if (!string.IsNullOrWhiteSpace(orNumber))
                attendance.SetORNumber(orNumber, recordedBy);
        }
        else
        {
            attendance.MarkUnpaid(recordedBy);
        }

        await uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
