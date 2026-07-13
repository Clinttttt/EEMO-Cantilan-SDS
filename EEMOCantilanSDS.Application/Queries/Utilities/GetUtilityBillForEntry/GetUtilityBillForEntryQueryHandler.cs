using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;

public class GetUtilityBillForEntryQueryHandler(
    IUtilityBillRepository utilityRepository,
    IFeeRateResolver feeRateResolver)
    : IRequestHandler<GetUtilityBillForEntryQuery, Result<UtilityBillEntryDto>>
{
    public async Task<Result<UtilityBillEntryDto>> Handle(GetUtilityBillForEntryQuery request, CancellationToken ct)
    {
        // Editing an already-recorded month → return its own readings/rates.
        var current = await utilityRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct);
        if (current is not null)
        {
            return Result<UtilityBillEntryDto>.Success(new UtilityBillEntryDto(
                true,
                current.ElecPreviousReading, current.ElecCurrentReading, current.ElecRatePerKwh,
                current.WaterPreviousReading, current.WaterCurrentReading, current.WaterRatePerCubicMeter,
                current.ElecStatus.ToString(), current.ElecPartialAmount,
                current.WaterStatus.ToString(), current.WaterPartialAmount,
                current.ElecORNumber, current.WaterORNumber));
        }

        // New month → carry the previous readings forward from the last bill's current readings, and
        // pre-fill the rate. Current starts equal to previous (zero consumption) until the admin enters
        // the new meter reading. The rate default is data-driven: carry the last bill's rate if it was
        // set, otherwise fall back to the tenant's configured ordinance rate (ElecPerKwh /
        // WaterPerCubicMeter FacilityRate, resolved as-of the bill month). A tenant with no such rows
        // (e.g. Cantilan) resolves to 0 and is left unchanged.
        var prior = await utilityRepository.GetLatestBeforeAsync(request.StallId, request.Year, request.Month, ct);
        var elecPrev = prior?.ElecCurrentReading ?? 0m;
        var waterPrev = prior?.WaterCurrentReading ?? 0m;

        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));
        var elecRate = prior is { ElecRatePerKwh: > 0m } ? prior.ElecRatePerKwh : snapshot.Resolve(FeeRateKey.ElecPerKwh, asOf);
        var waterRate = prior is { WaterRatePerCubicMeter: > 0m } ? prior.WaterRatePerCubicMeter : snapshot.Resolve(FeeRateKey.WaterPerCubicMeter, asOf);

        return Result<UtilityBillEntryDto>.Success(new UtilityBillEntryDto(
            false,
            elecPrev, elecPrev, elecRate,
            waterPrev, waterPrev, waterRate,
            "Unpaid", 0m,
            "Unpaid", 0m,
            null, null));
    }
}
