using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;

public class GetUtilityBillForEntryQueryHandler(IUtilityBillRepository utilityRepository)
    : IRequestHandler<GetUtilityBillForEntryQuery, Result<UtilityBillEntryDto>>
{
    public async Task<Result<UtilityBillEntryDto>> Handle(GetUtilityBillForEntryQuery request, CancellationToken ct)
    {
        // Editing an already-recorded month → return its own figures, and the bases each part may still take.
        var current = await utilityRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct);
        if (current is not null)
        {
            return Result<UtilityBillEntryDto>.Success(new UtilityBillEntryDto(
                true,
                current.ElecPreviousReading, current.ElecCurrentReading, current.ElecRatePerKwh,
                current.WaterPreviousReading, current.WaterCurrentReading, current.WaterRatePerCubicMeter,
                current.ElecStatus.ToString(), current.ElecPartialAmount,
                current.WaterStatus.ToString(), current.WaterPartialAmount,
                current.ElecORNumber, current.WaterORNumber,
                current.ElecCalculationBasis.ToString(), current.WaterCalculationBasis.ToString(),
                Names(current.AllowedCalculationBases(CollectionSourcePart.Electricity)),
                Names(current.AllowedCalculationBases(CollectionSourcePart.Water))));
        }

        // New month → IA-055: a new assessment is a direct approved amount only. Nothing is carried forward from an
        // earlier meter and no per-unit rate is suggested: neither is evidence for a direct amount, and the writer would
        // refuse them. The office states the approved amount.
        var direct = nameof(UtilityCalculationBasis.DirectApproved);
        var newBases = Names(UtilityBill.NewAssessmentBases);
        return Result<UtilityBillEntryDto>.Success(new UtilityBillEntryDto(
            false,
            0m, 0m, 0m,
            0m, 0m, 0m,
            "Unpaid", 0m,
            "Unpaid", 0m,
            null, null,
            direct, direct,
            newBases, newBases));
    }

    private static IReadOnlyList<string> Names(IReadOnlyList<UtilityCalculationBasis> bases) =>
        bases.Select(b => b.ToString()).ToList();
}
