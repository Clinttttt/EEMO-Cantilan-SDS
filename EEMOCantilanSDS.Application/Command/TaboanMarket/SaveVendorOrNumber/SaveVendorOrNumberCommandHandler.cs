using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.SaveVendorOrNumber;

public class SaveVendorOrNumberCommandHandler(
    ITpmRepository tpmRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SaveVendorOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveVendorOrNumberCommand request, CancellationToken ct)
    {
        var attendance = await tpmRepo.GetAttendanceByIdAsync(request.AttendanceId, ct);
        if (attendance == null)
            return Result<bool>.NotFound();

        attendance.SetORNumber(request.ORNumber);
        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            FacilityCode.TPM,
            attendance.MarketDate.Year,
            attendance.MarketDate.Month,
            ct);

        return Result<bool>.Success(true);
    }
}
