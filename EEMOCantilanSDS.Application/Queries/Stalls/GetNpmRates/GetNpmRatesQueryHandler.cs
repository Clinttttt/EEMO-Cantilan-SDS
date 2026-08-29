using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;

/// <summary>
/// Resolves the current tenant's NPM daily-stall and fish-per-kilo rates as of today. Uses the same
/// <see cref="IFeeRateResolver"/> snapshot as every billing path, so the value shown in the UI is exactly
/// what NPM is billed at — and falls back to the ordinance constants, leaving Cantilan unchanged.
/// </summary>
public class GetNpmRatesQueryHandler(
    IFeeRateResolver feeRateResolver,
    ICurrentUserService currentUser,
    IAppDbContext context, IClock clock)
    : IRequestHandler<GetNpmRatesQuery, Result<NpmRatesDto>>
{
    public async Task<Result<NpmRatesDto>> Handle(GetNpmRatesQuery request, CancellationToken ct)
    {
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = DateOnly.FromDateTime(clock.PhilippineNow);
        var daily = snapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var fish = snapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);

        // The rent a market space is let for. 0 means the LGU has not stated one, so the system charges thirty of
        // its daily fee — correct where the ordinance follows that convention, and a figure the office should be
        // asked to confirm where it does not.
        var monthly = snapshot.Resolve(FeeRateKey.NpmMonthlyStall, asOf);
        var monthlyInUse = monthly > 0m ? monthly : daily * DomainRules.DailyBilledMonthDays;

        // The reference tenant is never asked: the ordinance constants this platform derives from ARE its ordinance,
        // so thirty of its daily fee is the figure on its own paper — ₱30 × 30 = ₱900 — and there is nothing to
        // confirm. Exempting it needs POSITIVE proof: the caller's own municipality row, marked as the default one.
        // A request that carries no municipality claim resolves to the default TENANT CODE by a platform-wide
        // fallback, so a code comparison would have exempted it too — and the question would then go unasked for
        // the very LGU that most needs it. Asking is the safe direction, so anything unproven is asked.
        var isReferenceTenant = currentUser.MunicipalityId is { } municipalityId
            && await context.Municipalities.AnyAsync(m => m.Id == municipalityId && m.IsDefault, ct);

        return Result<NpmRatesDto>.Success(new NpmRatesDto(
            daily, fish, monthly, monthlyInUse,
            IsMonthlyRentConfirmed: monthly > 0m,
            NeedsMonthlyRentConfirmation: monthly <= 0m && !isReferenceTenant,
            // What a stall in each of the three areas is billed: the area's own rate where the office prices it apart,
            // else the market's. Asked of NpmDailyFee rather than read key by key, so a screen stating an area's rate and
            // the collector charging for a stall in it cannot answer by different rules.
            VegetableAreaDailyRate: NpmDailyFee.ForAreaOrNull(MarketSection.VegetableArea, snapshot, asOf) ?? 0m,
            FishSectionDailyRate: NpmDailyFee.ForAreaOrNull(MarketSection.FishSection, snapshot, asOf) ?? 0m,
            MeatSectionDailyRate: NpmDailyFee.ForAreaOrNull(MarketSection.MeatSection, snapshot, asOf) ?? 0m));
    }
}