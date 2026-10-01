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
            return Result<UtilityBillDto>.Failure("Utility billing applies to New Public Market stalls only.", ResultStatus.Invalid);

        var actor = currentUser.Username ?? "Admin";

        // A direct approved amount is stored as one unit at that amount, so the charge stays exact everywhere it is read.
        var elecDirect = request.ElecApprovedAmount.HasValue;
        var waterDirect = request.WaterApprovedAmount.HasValue;
        var (elecPrev, elecCurr, elecRate) = elecDirect
            ? UtilityBill.DirectApprovedReadings(request.ElecApprovedAmount!.Value)
            : (request.ElecPreviousReading, request.ElecCurrentReading, request.ElecRatePerKwh);
        var (waterPrev, waterCurr, waterRate) = waterDirect
            ? UtilityBill.DirectApprovedReadings(request.WaterApprovedAmount!.Value)
            : (request.WaterPreviousReading, request.WaterCurrentReading, request.WaterRatePerCubicMeter);
        var elecBasis = elecDirect ? UtilityCalculationBasis.DirectApproved : UtilityCalculationBasis.Metered;
        var waterBasis = waterDirect ? UtilityCalculationBasis.DirectApproved : UtilityCalculationBasis.Metered;

        var bill = await utilityRepository.GetByStallAndMonthAsync(request.StallId, request.BillingYear, request.BillingMonth, ct);

        // IA-055: a new assessment is a direct approved amount only. A recorded metered assessment is kept exactly as
        // recorded (or restated as a direct amount); new readings or per-unit rates are refused here, at the writer,
        // whatever the client offers.
        var refusal = UtilityBill.RefuseAssessment(bill, CollectionSourcePart.Electricity, elecDirect,
                          request.ElecPreviousReading, request.ElecCurrentReading, request.ElecRatePerKwh)
                      ?? UtilityBill.RefuseAssessment(bill, CollectionSourcePart.Water, waterDirect,
                          request.WaterPreviousReading, request.WaterCurrentReading, request.WaterRatePerCubicMeter);
        if (refusal is not null)
            return Result<UtilityBillDto>.Failure(refusal, ResultStatus.Invalid);

        if (bill is null)
        {
            bill = UtilityBill.Create(
                request.StallId, request.BillingYear, request.BillingMonth,
                elecPrev, elecCurr, elecRate, waterPrev, waterCurr, waterRate, actor);
            bill.SetCalculationBasis(elecBasis, waterBasis);
            await utilityRepository.AddAsync(bill, ct);
        }
        else
        {
            // Once a utility is settled or has entered cutover, its assessment facts are frozen. A converted
            // source must not be changed through this legacy assessment writer; the still-Legacy utility part
            // on the same bill remains independently editable.
            if (bill.WouldChangeSettledReadings(elecPrev, elecCurr, elecRate, waterPrev, waterCurr, waterRate)
                || bill.WouldChangeSettledBasis(elecBasis, waterBasis))
                return Result<UtilityBillDto>.Failure(
                    "Readings can't be changed for a settled or cutover utility source. Resolve the source through its approved workflow first.", ResultStatus.Conflict);

            bill.UpdateReadings(elecPrev, elecCurr, elecRate, waterPrev, waterCurr, waterRate, request.Remarks, actor);
            bill.SetCalculationBasis(elecBasis, waterBasis);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, request.BillingYear, request.BillingMonth, ct);

        return Result<UtilityBillDto>.Success(UtilityBillDto.From(bill));
    }
}
