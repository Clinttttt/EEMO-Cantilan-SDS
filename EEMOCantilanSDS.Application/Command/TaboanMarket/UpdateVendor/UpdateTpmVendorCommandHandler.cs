using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.UpdateVendor;

public class UpdateTpmVendorCommandHandler(
    ITpmRepository tpmRepo,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<UpdateTpmVendorCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateTpmVendorCommand request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetAttendanceByIdAsync(request.AttendanceId, ct);
        if (attendance == null)
            return Result<bool>.NotFound();

        // Collectors may only edit at Tabo-an if assigned to it; admins are unrestricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<bool>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.TPM))
            {
                return Result<bool>.Forbidden();
            }
        }

        var vendor = await tpmRepo.GetVendorByIdAsync(attendance.VendorId, ct);
        if (vendor == null)
            return Result<bool>.NotFound();

        var recordedBy = currentUser.Username ?? "Admin";

        // Vendor-level details (shared across every market day for this vendor).
        vendor.UpdateDetails(request.VendorName, request.Goods, vendor.ContactNumber, vendor.Remarks, recordedBy);

        // Per-day attendance status + OR number.
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
