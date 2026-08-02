using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Tenancy;
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
public class GetNpmRatesQueryHandler(IFeeRateResolver feeRateResolver, ITenantContext tenantContext)
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

        // The reference tenant is never asked: the ordinance constants this platform derives from ARE its ordinance,
        // so thirty of its daily fee is the figure on its own paper — ₱30 × 30 = ₱900 — and there is nothing to
        // confirm. Every other LGU passed its own ordinance and is asked once.
        var isReferenceTenant = string.Equals(
            tenantContext.TenantCode, TenantConstants.DefaultTenantCode, StringComparison.OrdinalIgnoreCase);

        return Result<NpmRatesDto>.Success(new NpmRatesDto(
            daily, fish, monthly, monthlyInUse,
            IsMonthlyRentConfirmed: monthly > 0m,
            NeedsMonthlyRentConfirmation: monthly <= 0m && !isReferenceTenant));
    }
}
