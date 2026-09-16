using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmOverview;

public class GetTpmOverviewQueryHandler(
    ITpmRepository tpmRepo,
    IFeeRateResolver feeRateResolver,
    IClock clock) : IRequestHandler<GetTpmOverviewQuery, Result<TpmOverviewDto>>
{
    public async Task<Result<TpmOverviewDto>> Handle(GetTpmOverviewQuery request, CancellationToken ct)
    {
        var overview = await tpmRepo.GetOverviewAsync(request.Year, request.Month, ct);
        // This LGU's own per-vendor fee, on the day a vendor added now would be charged it — see RatePeriod. Asking for
        // the first of the month showed the fee as it stood before any edit made during that month, while adding a vendor
        // resolves at the market date.
        var asOf = RatePeriod.AsOf(request.Year, request.Month, clock.PhilippineToday);
        var vendorFee = (await feeRateResolver.GetSnapshotAsync(ct)).Resolve(FeeRateKey.TpmVendorDay, asOf);
        return Result<TpmOverviewDto>.Success(overview with { VendorFee = vendorFee });
    }
}
