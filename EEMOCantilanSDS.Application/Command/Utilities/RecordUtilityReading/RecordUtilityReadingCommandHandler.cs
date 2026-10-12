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

        // A utility amount is set only for a verified service source: an NPM stall as always, or a space of any facility whose own
        // service flag for that utility is on. Nothing is inferred from a name, an occupant or a vendor registration.
        var wantsElectricity = request.ElecApprovedAmount.HasValue || request.ElecCurrentReading != request.ElecPreviousReading;
        var wantsWater = request.WaterApprovedAmount.HasValue || request.WaterCurrentReading != request.WaterPreviousReading;
        if (!UtilityServiceEligibility.IsSource(stall, UtilityKind.Electricity) && !UtilityServiceEligibility.IsSource(stall, UtilityKind.Water))
            return Result<UtilityBillDto>.Failure("Utility amounts apply to spaces with a verified electricity or water service only.", ResultStatus.Invalid);
        if (wantsElectricity && !UtilityServiceEligibility.IsSource(stall, UtilityKind.Electricity))
            return Result<UtilityBillDto>.Failure("This space has no verified electricity service.", ResultStatus.Invalid);
        if (wantsWater && !UtilityServiceEligibility.IsSource(stall, UtilityKind.Water))
            return Result<UtilityBillDto>.Failure("This space has no verified water service.", ResultStatus.Invalid);

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

        // A part sent back exactly as recorded keeps its recorded basis (a direct amount is never relabelled Metered).
        var elecBasis = UtilityBill.BasisFor(bill, CollectionSourcePart.Electricity, elecDirect, elecPrev, elecCurr, elecRate);
        var waterBasis = UtilityBill.BasisFor(bill, CollectionSourcePart.Water, waterDirect, waterPrev, waterCurr, waterRate);

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
        // The views of the space's own facility (a utility source may be any facility's); unknown facility falls back to the whole period.
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, stall.Facility?.Code, request.BillingYear, request.BillingMonth, ct);

        return Result<UtilityBillDto>.Success(UtilityBillDto.From(bill));
    }
}
