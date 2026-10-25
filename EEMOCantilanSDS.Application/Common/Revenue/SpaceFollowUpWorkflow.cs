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
    public async Task<IReadOnlyList<ObligationFollowUpItemDto>> GetAsync(DateOnly asOf, string? operationCode, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin") || tenant.MunicipalityId == Guid.Empty ||
            user.MunicipalityId is { } claimed && claimed != tenant.MunicipalityId) return [];
        var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenant.MunicipalityId &&
            (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.FiestaArawLotRental || x.Kind == ObligationKind.VegetableFruitSpaceRental)).ToListAsync(ct);
        var byId = accounts.ToDictionary(x => x.Id);
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(tenant.MunicipalityId, accounts, asOf, ct);
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        // A renewed space follows up its CURRENT term only. What an earlier term left owing stays on the account (still collectible) and is
        // stated as that term's balance on Closed Accounts → Renewed, the same way a renewed stall's earlier contract is. A never-renewed
        // account, expired or not, has no appended term and is followed up exactly as before.
        var ids = accounts.Select(x => x.Id).ToArray();
        var currentTermStart = (await db.SpaceContractTerms.AsNoTracking()
                .Where(x => x.MunicipalityId == tenant.MunicipalityId && ids.Contains(x.ObligationAccountId)).ToListAsync(ct))
            .GroupBy(x => x.ObligationAccountId)
            .ToDictionary(g => g.Key, g => SpaceTermSplit.CurrentTermStart(SpaceTermSplit.Ordered(g))!.Value);
        return quotes.Where(x => x.OutstandingAmount > 0m && (ObligationAccount.IsMonthlySpaceKind(x.Kind) ? x.PeriodStart < month : x.PeriodStart < asOf)
                && !(currentTermStart.TryGetValue(x.AccountId, out var termStart) && x.PeriodStart < termStart))
            .Select(x =>
            {
                var account = byId[x.AccountId];
                var monthly = ObligationAccount.IsMonthlySpaceKind(x.Kind);
                var code = ObligationAccount.OperationCodeFor(x.Kind)!;
                return new ObligationFollowUpItemDto(x.AccountId, x.PayorId, x.PayerName, x.SubjectLabel,
                    new(monthly ? FollowUpScopeKind.MonthlySpace : FollowUpScopeKind.EventLot, code, x.KindLabel),
                    x.PeriodStart, x.AssessedAmount, x.SettledAmount, x.OutstandingAmount,
                    monthly && x.PeriodStart <= month.AddMonths(-3) ? "Critical" : "Normal", account.Event,
                    code == CollectorOperationCodes.VegetableFruitSpaceRental ? "/operations/vegetable-fruit" : monthly ? "/operations/kanmanggay" : "/operations/fiesta-araw");
            }).Where(x => operationCode is null || x.Scope.OperationCode == operationCode).ToArray();
    }
}
