using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTrmOverview;

public class GetTrmOverviewQueryHandler(
    ITrmRepository trmRepo,
    IFeeRateResolver feeRateResolver) : IRequestHandler<GetTrmOverviewQuery, Result<TrmOverviewDto>>
{
    public async Task<Result<TrmOverviewDto>> Handle(GetTrmOverviewQuery request, CancellationToken ct)
    {
        var overview = await trmRepo.GetOverviewAsync(ct);
        // Stamp the tenant's resolved per-trip fee (as of today) so the UI shows this LGU's own rate.
        var tripFee = (await feeRateResolver.GetSnapshotAsync(ct)).Resolve(FeeRateKey.TrmPerTrip, PhilippineTime.Today);
        return Result<TrmOverviewDto>.Success(overview with { TripFee = tripFee });
    }
}
