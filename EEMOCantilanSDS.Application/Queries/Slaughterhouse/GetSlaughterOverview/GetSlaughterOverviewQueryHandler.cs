using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;

public class GetSlaughterOverviewQueryHandler(
    ISlaughterRepository slaughterRepository,
    IFeeRateResolver feeRateResolver) : IRequestHandler<GetSlaughterOverviewQuery, Result<SlaughterOverviewDto>>
{
    public async Task<Result<SlaughterOverviewDto>> Handle(GetSlaughterOverviewQuery request, CancellationToken ct)
    {
        var overview = await slaughterRepository.GetOverviewAsync(request.Year, request.Month, ct);
        // The office's own per-head rates as of the report month, or null where its ordinance states none.
        // ResolveOrNull, not Resolve: Resolve reads an unstated rate as zero, and a zero rate is indistinguishable from
        // an office that charges nothing, so the screen offered animals nobody had priced. The recording handler
        // already refuses a transaction whose per-head rate is unstated; this is the same rule, one screen earlier.
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(request.Year, request.Month, 1);
        return Result<SlaughterOverviewDto>.Success(overview with
        {
            HogRatePerHead = snapshot.ResolveOrNull(FeeRateKey.SlhHogPerHead, asOf),
            LargeRatePerHead = snapshot.ResolveOrNull(FeeRateKey.SlhLargePerHead, asOf)
        });
    }
}
