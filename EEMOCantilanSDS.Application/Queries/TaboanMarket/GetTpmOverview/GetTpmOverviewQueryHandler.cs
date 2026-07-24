using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmOverview;

public class GetTpmOverviewQueryHandler(
    ITpmRepository tpmRepo,
    IFeeRateResolver feeRateResolver) : IRequestHandler<GetTpmOverviewQuery, Result<TpmOverviewDto>>
{
    public async Task<Result<TpmOverviewDto>> Handle(GetTpmOverviewQuery request, CancellationToken ct)
    {
        var overview = await tpmRepo.GetOverviewAsync(request.Year, request.Month, ct);
        // Stamp the tenant's resolved per-vendor fee (as of the report month) so the UI shows this LGU's own
        // rate. Fallback inside the snapshot keeps Cantilan at ₱100.
        var asOf = new DateOnly(request.Year, request.Month, 1);
        var vendorFee = (await feeRateResolver.GetSnapshotAsync(ct)).Resolve(FeeRateKey.TpmVendorDay, asOf);
        return Result<TpmOverviewDto>.Success(overview with { VendorFee = vendorFee });
    }
}
