using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileUnpaidMonths;

/// <summary>The unpaid months of one stall's CURRENT contract that are still collected on the facility's own monthly operation.</summary>
public sealed record GetMobileUnpaidMonthsQuery(Guid StallId) : IRequest<Result<IReadOnlyList<MobileUnpaidMonthDto>>>;

/// <summary>
/// A collector sees a stall's unpaid months only for a facility they are assigned to, and only the rows whose money is still authoritative on the legacy monthly
/// record: a canonical or transitioning row is collected through New Collection, never offered here. Only months that have fully elapsed are listed (the month in progress is collected on the facility's monthly screen, and is not yet delinquent). Nothing is computed:
/// each figure is the monthly record's own.
/// </summary>
public sealed class GetMobileUnpaidMonthsQueryHandler(
    IStallRepository stalls, ICollectorRepository collectors, ICurrentUserService user, IStallLedgerQueries ledger, IClock clock)
    : IRequestHandler<GetMobileUnpaidMonthsQuery, Result<IReadOnlyList<MobileUnpaidMonthDto>>>
{
    public async Task<Result<IReadOnlyList<MobileUnpaidMonthDto>>> Handle(GetMobileUnpaidMonthsQuery request, CancellationToken ct)
    {
        if (user.Role != "Collector" || user.CollectorId is not { } collectorId) return Result<IReadOnlyList<MobileUnpaidMonthDto>>.Forbidden();
        var stall = await stalls.GetByIdAsync(request.StallId, ct);
        if (stall?.Facility is null) return Result<IReadOnlyList<MobileUnpaidMonthDto>>.NotFound();
        var collector = await collectors.GetByIdAsync(collectorId, ct);
        if (collector is null || !collector.FacilityAssignments.Any(a => a.FacilityCode == stall.Facility.Code)) return Result<IReadOnlyList<MobileUnpaidMonthDto>>.Forbidden();
        // A daily-billed market is collected from its daily calendar; there is no monthly record to settle.
        if (stall.Facility.Code == FacilityCode.NPM && !stall.Facility.IsMonthlyRentMarket) return Result<IReadOnlyList<MobileUnpaidMonthDto>>.Success([]);

        var today = clock.PhilippineToday;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var rows = await ledger.GetOutstandingMonthsAsync(request.StallId, null, null, ct);
        var months = rows
            .Where(r => r.BalanceDue > 0m && r.SettlementAuthority == SettlementAuthority.Legacy
                && DateOnly.TryParseExact(r.Period + "-01", "yyyy-MM-dd", out var start) && start < thisMonth)
            .Select(r => new MobileUnpaidMonthDto(int.Parse(r.Period[..4]), int.Parse(r.Period[5..7]), r.TotalBill, r.AmountPaid, r.BalanceDue))
            .OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();
        return Result<IReadOnlyList<MobileUnpaidMonthDto>>.Success(months);
    }
}
