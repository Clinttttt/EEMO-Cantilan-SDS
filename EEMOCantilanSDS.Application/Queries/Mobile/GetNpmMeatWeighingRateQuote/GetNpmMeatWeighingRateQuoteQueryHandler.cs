using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetNpmMeatWeighingRateQuote;

public sealed class GetNpmMeatWeighingRateQuoteQueryHandler(IFeeRateResolver rates, IClock clock)
    : IRequestHandler<GetNpmMeatWeighingRateQuoteQuery, Result<NpmMeatWeighingRateQuoteDto>>
{
    public async Task<Result<NpmMeatWeighingRateQuoteDto>> Handle(
        GetNpmMeatWeighingRateQuoteQuery request, CancellationToken ct)
    {
        if (request.BusinessDate == DateOnly.MinValue || request.BusinessDate > clock.PhilippineToday)
            return Result<NpmMeatWeighingRateQuoteDto>.Failure(
                "Choose a valid collection business date that is not in the future.", ResultStatus.Invalid);

        var entry = (await rates.GetSnapshotAsync(ct))
            .ResolveEntryOrNull(FeeRateKey.NpmMeatPerKilo, request.BusinessDate);
        if (entry is not { Amount: > 0m })
            return Result<NpmMeatWeighingRateQuoteDto>.Success(
                new(request.BusinessDate, false, null, null));

        return Result<NpmMeatWeighingRateQuoteDto>.Success(
            new(request.BusinessDate, true, entry.Value.Amount, entry.Value.EffectiveDate));
    }
}
