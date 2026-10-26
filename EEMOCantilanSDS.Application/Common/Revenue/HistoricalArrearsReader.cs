using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The Collector Mobile Arrears and Delinquency workspace: one row per liable ACCOUNT, composed from the SAME readers the Head's Arrears page and the
/// Financial Report read (the delinquency source for live stall accounts, the inactive-account register for ended contracts, the space follow-up feed
/// for space rentals), so the totals agree by construction. It reads only: it assesses nothing, posts nothing and moves no balance. Stall rent is keyed
/// to the ORIGINAL contract and space rental to the obligation account, so a successor never carries a predecessor's debt.
/// </summary>
public sealed class HistoricalArrearsReader(
    IFacilityReportsRepository reports, IClosedStallAccountQueries closed, SpaceFollowUpWorkflow space, IAppDbContext db,
    ICurrentUserService user, ICurrentMunicipalityAccessor tenant, IClock clock)
{
    public async Task<ArrearsWorkspaceDto> GetForCollectorAsync(CancellationToken ct)
    {
        var today = clock.PhilippineToday;
        var empty = new ArrearsWorkspaceDto(today, [], 0m, 0m, 0m);
        var tenantId = tenant.MunicipalityId;
        if (!user.IsAuthenticated || user.Role != "Collector" || user.CollectorId is not { } collectorId || tenantId == Guid.Empty ||
            user.MunicipalityId is { } claimed && claimed != tenantId) return empty;
        var collector = await db.CollectorUsers.AsNoTracking().Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
        if (collector is null) return empty;
        var accounts = new List<ArrearsAccountDto>();

        var assigned = collector.FacilityAssignments.Select(a => a.FacilityCode).ToHashSet();
        var facilities = (await db.Facilities.AsNoTracking().Where(f => f.MunicipalityId == tenantId && f.IsActive).ToListAsync(ct))
            .Where(f => assigned.Contains(f.Code)).ToDictionary(f => f.Code);
        if (facilities.Count > 0)
        {
            // Live accounts: the present occupancy's own months (Delinquent) and its previous-year / expired-contract months (Arrears).
            var live = (await reports.GetDelinquentStallsAsync(null, today.Year, today.Month, includeClosed: true, wholeAccount: true, ct))
                .Where(d => facilities.ContainsKey(d.FacilityCode) && d.StallId.HasValue
                    && (d.DelinquentMonthCount >= DomainRules.DelinquentThresholdMonths || d.ArrearsOutstanding > 0m)).ToList();
            var stallIds = live.Select(d => d.StallId!.Value).ToArray();
            var stalls = await db.Stalls.AsNoTracking().Include(s => s.Contracts)
                .Where(s => s.MunicipalityId == tenantId && stallIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            foreach (var d in live)
            {
                if (!stalls.TryGetValue(d.StallId!.Value, out var stall) || stall.Occupancies(today).LastOrDefault()?.Contract is not { } contract) continue;
                var facility = facilities[d.FacilityCode];
                var delinquent = d.DelinquentMonthCount >= DomainRules.DelinquentThresholdMonths ? d.DelinquentOutstanding : 0m;
                var arrears = d.ArrearsOutstanding;
                if (delinquent + arrears <= 0m) continue;
                accounts.Add(StallRow(facility, d.StallNo, d.Section, d.Occupant, contract.Id, d.TermLapsed ? "Lapsed" : "Active",
                    arrears, delinquent, d.Classified ? d.ArrearsMonths + d.DelinquentMonths : d.MonthsUnpaid,
                    d.ArrearsFirstPeriod ?? d.DelinquentFirstPeriod, d.DelinquentLastPeriod ?? d.ArrearsLastPeriod,
                    arrears > 0m ? (d.TermLapsed ? SpaceArrears.ExpiredContract : SpaceArrears.PreviousYear) : null));
            }
            // Ended occupancies: a former lessee's own debt, kept with that contract. A lapsed-but-held account is the live one above.
            foreach (var a in await closed.GetClosedStallAccountsAsOfAsync(today, ct))
                if (a.Uncollected > 0m && a.State != InactiveAccountState.Lapsed && a.ContractId != Guid.Empty && facilities.TryGetValue(a.FacilityCode, out var facility))
                    accounts.Add(StallRow(facility, a.StallNo, a.Section, a.Occupant, a.ContractId, StatusOf(a), a.Uncollected, 0m, a.MonthsUnpaid, null, null, SpaceArrears.EndedOccupancy));
        }

        var items = await space.GetForCollectorAsync(today, ct);
        var spaceIds = items.Select(i => i.AccountId).Distinct().ToArray();
        var spaceAccounts = spaceIds.Length == 0 ? new Dictionary<Guid, ObligationAccount>()
            : await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenantId && spaceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        foreach (var g in items.GroupBy(i => i.AccountId))
        {
            var first = g.First();
            if (!spaceAccounts.TryGetValue(g.Key, out var account)) continue;
            var arrears = g.Where(i => i.ReasonKind == "arrears").Sum(i => i.Outstanding);
            var delinquent = g.Where(i => i.ReasonKind != "arrears").Sum(i => i.Outstanding);
            var prior = g.Where(i => i.EarlierTerm).Sum(i => i.Outstanding);
            var periods = g.Select(i => i.PeriodStart).OrderBy(p => p).ToArray();
            var status = account.ActiveTo is { } to && to < today ? "Closed"
                : prior > 0m ? "Renewed" : first.TermLastDay is { } last && last < today ? "Lapsed" : "Active";
            accounts.Add(new($"space:{g.Key:N}", ArrearsAccountKind.SpaceRental, new(SourceIdentityKind.SpaceAccount, g.Key), first.Scope.OperationCode,
                first.Scope.DisplayName, first.PayerName ?? account.ActualOccupant ?? first.SpaceNumber, first.SpaceNumber, null, status, ReasonOf(arrears, delinquent),
                arrears, delinquent, arrears + delinquent, g.Count(), $"{periods[0]:yyyy-MM}", $"{periods[^1]:yyyy-MM}", prior, arrears + delinquent - prior,
                g.Select(i => i.ArrearsBasis).FirstOrDefault(b => b is not null), true, null, g.Key));
        }

        var ordered = accounts.OrderByDescending(a => a.Total).ThenBy(a => a.Space, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.AccountKey, StringComparer.Ordinal).ToArray();
        return new(today, ordered, ordered.Sum(a => a.Total), ordered.Sum(a => a.ArrearsAmount), ordered.Sum(a => a.DelinquentAmount));
    }

    private static string StatusOf(ClosedStallAccountDto a) => a.State switch
    {
        InactiveAccountState.Closed => "Closed",
        InactiveAccountState.Renewed => "Renewed",
        _ => a.StallReLet ? "Re-let" : "Ended",
    };

    private static string ReasonOf(decimal arrears, decimal delinquent) => arrears > 0m && delinquent > 0m ? "both" : arrears > 0m ? "arrears" : "delinquent";

    private static ArrearsAccountDto StallRow(Domain.Entities.Facilities.Facility facility, string stallNo, string? section, string payer, Guid contractId,
        string status, decimal arrears, decimal delinquent, int months, string? first, string? last, string? basis) =>
        new($"contract:{contractId:N}", ArrearsAccountKind.StallRent, new(SourceIdentityKind.Occupancy, contractId), "FACILITY_" + facility.Code, facility.Name,
            string.IsNullOrWhiteSpace(payer) ? "Unnamed occupant" : payer, SpaceNumber.Describe(stallNo), string.IsNullOrWhiteSpace(section) ? null : section, status,
            ReasonOf(arrears, delinquent), arrears, delinquent, arrears + delinquent, months, first, last, 0m, arrears + delinquent, basis,
            MonthlyRentalFacilities.Bills(facility), contractId, null);
}
