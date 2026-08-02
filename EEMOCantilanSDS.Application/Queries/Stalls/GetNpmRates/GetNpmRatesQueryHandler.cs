using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;

/// <summary>
/// Resolves the current tenant's NPM daily-stall and fish-per-kilo rates as of today. Uses the same
/// <see cref="IFeeRateResolver"/> snapshot as every billing path, so the value shown in the UI is exactly
/// what NPM is billed at — and falls back to the ordinance constants, leaving Cantilan unchanged.
/// </summary>
public class GetNpmRatesQueryHandler(IFeeRateResolver feeRateResolver)
    : IRequestHandler<GetNpmRatesQuery, Result<NpmRatesDto>>
{
    public async Task<Result<NpmRatesDto>> Handle(GetNpmRatesQuery request, CancellationToken ct)
    {
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = DateOnly.FromDateTime(PhilippineTime.Now);
        var daily = snapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var fish = snapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);

        // The rent a market space is let for. 0 means the LGU has not stated one, so the system charges thirty of
        // its daily fee — correct where the ordinance follows that convention, and a figure the office should be
        // asked to confirm where it does not.
        var monthly = snapshot.Resolve(FeeRateKey.NpmMonthlyStall, asOf);
        var monthlyInUse = monthly > 0m ? monthly : daily * DomainRules.DailyBilledMonthDays;

        return Result<NpmRatesDto>.Success(new NpmRatesDto(daily, fish, monthly, monthlyInUse, monthly > 0m));
    }
}
