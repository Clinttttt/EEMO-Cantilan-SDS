using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;

public class AddVendorToMarketDayCommandHandler(
    ITpmRepository tpmRepo,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<AddVendorToMarketDayCommand, Result<TpmVendorAttendanceDto>>
{
    public async Task<Result<TpmVendorAttendanceDto>> Handle(AddVendorToMarketDayCommand request, CancellationToken ct)
    {
        // Collectors may only add vendors at Tabo-an if assigned to it; admins are unrestricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<TpmVendorAttendanceDto>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.TPM))
            {
                return Result<TpmVendorAttendanceDto>.Forbidden();
            }
        }

        var existingVendors = await tpmRepo.GetAllVendorsAsync(ct);
        var vendor = existingVendors.FirstOrDefault(v =>
            v.VendorName.Equals(request.VendorName, StringComparison.OrdinalIgnoreCase));

        if (vendor == null)
        {
            vendor = TpmVendor.Create(request.VendorName, request.Goods);
            await tpmRepo.AddVendorAsync(vendor, ct);
        }

        var existingAttendance = await tpmRepo.GetAttendanceAsync(vendor.Id, request.MarketDate, ct);
        if (existingAttendance != null)
            return Result<TpmVendorAttendanceDto>.Failure("Vendor already added to this market day.");

        var attendance = TpmAttendance.Create(vendor.Id, request.MarketDate);

        if (request.ClientOperationId is { } clientOpId)
            attendance.SetClientOperationId(clientOpId);

        // At Tabo-an, paying the ₱100 is what grants the vendor their area, so recording a vendor
        // inherently means the fee was collected — the attendance is paid on creation.
        var recordedBy = currentUser.Username ?? "Admin";
        attendance.MarkPaid(currentUser.CollectorId, updatedBy: recordedBy);
        if (!string.IsNullOrWhiteSpace(request.ORNumber))
            attendance.SetORNumber(request.ORNumber.Trim(), recordedBy);

        await tpmRepo.AddAttendanceAsync(attendance, ct);
        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            FacilityCode.TPM,
            request.MarketDate.Year,
            request.MarketDate.Month,
            ct);

        return Result<TpmVendorAttendanceDto>.Success(new TpmVendorAttendanceDto
        {
            Id = attendance.Id,
            VendorId = vendor.Id,
            VendorName = vendor.VendorName,
            Goods = vendor.Goods,
            IsPaid = attendance.IsPaid,
            ORNumber = attendance.ORNumber,
            Fee = attendance.Fee,
            MarketDate = attendance.MarketDate
        });
    }
}
