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
        // Stamp the tenant's resolved per-head rates (as of the report month) so the UI shows this LGU's own
        // rates; the snapshot falls back to the ₱250 / ₱365 ordinance constants (Cantilan unchanged).
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(request.Year, request.Month, 1);
        return Result<SlaughterOverviewDto>.Success(overview with
        {
            HogRatePerHead = snapshot.Resolve(FeeRateKey.SlhHogPerHead, asOf),
            LargeRatePerHead = snapshot.Resolve(FeeRateKey.SlhLargePerHead, asOf)
        });
    }
}
