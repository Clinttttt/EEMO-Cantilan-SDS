using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;

public class RecordUtilityReadingCommandHandler(
    IUtilityBillRepository utilityRepository,
    IStallRepository stallRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<RecordUtilityReadingCommand, Result<UtilityBillDto>>
{
    public async Task<Result<UtilityBillDto>> Handle(RecordUtilityReadingCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<UtilityBillDto>.NotFound();

        // Meter-based utility billing is an NPM concept only.
        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<UtilityBillDto>.Failure("Utility billing applies to New Public Market stalls only.", 400);

        var actor = currentUser.Username ?? "Admin";

        var bill = await utilityRepository.GetByStallAndMonthAsync(request.StallId, request.BillingYear, request.BillingMonth, ct);
        if (bill is null)
        {
            bill = UtilityBill.Create(
                request.StallId, request.BillingYear, request.BillingMonth,
                request.ElecPreviousReading, request.ElecCurrentReading, request.ElecRatePerKwh,
                request.WaterPreviousReading, request.WaterCurrentReading, request.WaterRatePerCubicMeter,
                actor);
            await utilityRepository.AddAsync(bill, ct);
        }
        else
        {
            bill.UpdateReadings(
                request.ElecPreviousReading, request.ElecCurrentReading, request.ElecRatePerKwh,
                request.WaterPreviousReading, request.WaterCurrentReading, request.WaterRatePerCubicMeter,
                request.Remarks, actor);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, request.BillingYear, request.BillingMonth, ct);

        return Result<UtilityBillDto>.Success(UtilityBillDto.From(bill));
    }
}
