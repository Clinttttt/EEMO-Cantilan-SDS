using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;

public class GetSlaughterOverviewQueryHandler(
    ISlaughterRepository slaughterRepository,
    IFeeRateResolver feeRateResolver,
    IClock clock) : IRequestHandler<GetSlaughterOverviewQuery, Result<SlaughterOverviewDto>>
{
    public async Task<Result<SlaughterOverviewDto>> Handle(GetSlaughterOverviewQuery request, CancellationToken ct)
    {
        var overview = await slaughterRepository.GetOverviewAsync(request.Year, request.Month, ct);
        // The office's own per-head rates, as of the latest date in the report month that has actually arrived.
        //
        // A rate edit takes effect from the day it is made and is never retroactive, so asking for the FIRST of the
        // month answered with whatever the ordinance said before the edit: an office that raised the hog fee to ₱251 on
        // the 15th was still shown ₱250 on the 16th, while recording a transaction that same day charged ₱251 — the
        // recording handler resolves at the TRANSACTION date. A screen that quotes a fee must quote the fee the
        // transaction it is about to record will carry.
        //
        // Clamped into the month so a past period still states the rate in force when it closed, rather than today's.
        //
        // ResolveOrNull, not Resolve: Resolve reads an unstated rate as zero, and a zero rate is indistinguishable from
        // an office that charges nothing, so the screen offered animals nobody had priced. The recording handler
        // already refuses a transaction whose per-head rate is unstated; this is the same rule, one screen earlier.
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = RatePeriod.AsOf(request.Year, request.Month, clock.PhilippineToday);
        return Result<SlaughterOverviewDto>.Success(overview with
        {
            HogRatePerHead = snapshot.ResolveOrNull(FeeRateKey.SlhHogPerHead, asOf),
            LargeRatePerHead = snapshot.ResolveOrNull(FeeRateKey.SlhLargePerHead, asOf)
        });
    }
}
