using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;

public class GetUtilityBillForEntryQueryHandler(IUtilityBillRepository utilityRepository)
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
        // pre-fill the last rates. Current starts equal to previous (zero consumption) until the admin
        // enters the new meter reading.
        var prior = await utilityRepository.GetLatestBeforeAsync(request.StallId, request.Year, request.Month, ct);
        var elecPrev = prior?.ElecCurrentReading ?? 0m;
        var waterPrev = prior?.WaterCurrentReading ?? 0m;

        return Result<UtilityBillEntryDto>.Success(new UtilityBillEntryDto(
            false,
            elecPrev, elecPrev, prior?.ElecRatePerKwh ?? 0m,
            waterPrev, waterPrev, prior?.WaterRatePerCubicMeter ?? 0m,
            "Unpaid", 0m,
            "Unpaid", 0m,
            null, null));
    }
}
