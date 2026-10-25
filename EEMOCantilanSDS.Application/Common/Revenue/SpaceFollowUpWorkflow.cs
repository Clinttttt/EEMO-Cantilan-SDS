using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed class SpaceFollowUpWorkflow(IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor tenant)
{
    /// <summary>
    /// Every unpaid, elapsed space period, each classified on its own by the stall rules (IA-019, IA-020, IA-069): Arrears when it is of a previous
    /// calendar year, of an ended occupancy, or of an expired contract (a signed term that has run out, or an earlier term of a renewed space);
    /// otherwise Delinquent. Nothing is assessed or recalculated here — each figure is the period's own canonical balance.
    /// </summary>
    /// <param name="includeEarlierTerms">
    /// The live queue follows up a renewed space's CURRENT term only (false): what an earlier term left owing stays on the account, still
    /// collectible, and is stated as that term's balance on Closed Accounts → Renewed. The whole-time history and the Arrears register state it too
    /// (true), flagged <see cref="ObligationFollowUpItemDto.EarlierTerm"/>, so it is never lost and never mistaken for the current term's debt.
    /// </param>
    public async Task<IReadOnlyList<ObligationFollowUpItemDto>> GetAsync(DateOnly asOf, string? operationCode, CancellationToken ct, bool includeEarlierTerms = false)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin") || tenant.MunicipalityId == Guid.Empty ||
            user.MunicipalityId is { } claimed && claimed != tenant.MunicipalityId) return [];
        var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenant.MunicipalityId &&
            (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.FiestaArawLotRental || x.Kind == ObligationKind.VegetableFruitSpaceRental)).ToListAsync(ct);
        var byId = accounts.ToDictionary(x => x.Id);
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(tenant.MunicipalityId, accounts, asOf, ct);
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        var ids = accounts.Select(x => x.Id).ToArray();
        var terms = (await db.SpaceContractTerms.AsNoTracking()
                .Where(x => x.MunicipalityId == tenant.MunicipalityId && ids.Contains(x.ObligationAccountId)).ToListAsync(ct))
            .GroupBy(x => x.ObligationAccountId)
            .ToDictionary(g => g.Key, g => SpaceTermSplit.Ordered(g));

        return quotes.Where(x => x.OutstandingAmount > 0m && (ObligationAccount.IsMonthlySpaceKind(x.Kind) ? x.PeriodStart < month : x.PeriodStart < asOf))
            .Select(x =>
            {
                var account = byId[x.AccountId];
                var monthly = ObligationAccount.IsMonthlySpaceKind(x.Kind);
                var code = ObligationAccount.OperationCodeFor(x.Kind)!;
                var accountTerms = terms.GetValueOrDefault(x.AccountId) ?? [];
                var earlier = SpaceTermSplit.CurrentTermStart(accountTerms) is { } termStart && x.PeriodStart < termStart;
                var termLastDay = SpaceArrears.TermLastDay(account, accountTerms);
                var basis = SpaceArrears.Basis(account, accountTerms, x.PeriodStart, asOf);
                return new ObligationFollowUpItemDto(x.AccountId, x.PayorId, x.PayerName, x.SubjectLabel,
                    new(monthly ? FollowUpScopeKind.MonthlySpace : FollowUpScopeKind.EventLot, code, x.KindLabel),
                    x.PeriodStart, x.AssessedAmount, x.SettledAmount, x.OutstandingAmount,
                    monthly && x.PeriodStart <= month.AddMonths(-3) ? "Critical" : "Normal", account.Event,
                    code == CollectorOperationCodes.VegetableFruitSpaceRental ? "/operations/vegetable-fruit" : monthly ? "/operations/kanmanggay" : "/operations/fiesta-araw",
                    basis is null ? "delinquent" : "arrears", basis, earlier, termLastDay);
            })
            .Where(x => (includeEarlierTerms || !x.EarlierTerm) && (operationCode is null || x.Scope.OperationCode == operationCode)).ToArray();
    }
}
